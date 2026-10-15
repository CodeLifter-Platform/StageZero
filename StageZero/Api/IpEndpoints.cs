using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using StageZero.DataAdapters.IpChecks;
using StageZero.Models;
using StageZero.Services.IpMonitoring;

namespace StageZero.Api;

/// <summary>
/// The public IP: current value, history, a manual check, and a push stream of changes.
/// All of it behind a sign-in — a Cloudflare Tunnel exists to hide this address.
/// </summary>
public static class IpEndpoints
{
    /// <summary>The first event on every stream: the IP as it stands, so a listener starts current.</summary>
    public const string CurrentEvent = "ip-current";

    public const string ChangedEvent = "ip-changed";

    public static void MapIpEndpoints(this IEndpointRouteBuilder api)
    {
        var ip = api.MapGroup("/ip").RequireAuthorization(Session.OperatorPolicy).WithUserFacingErrors();

        ip.MapGet("/current", async (IIpCheckReader reader) => Current(await reader.GetLatestAsync()));
        ip.MapGet("/history", (IIpCheckReader reader) => reader.GetGroupedByIpAsync());
        ip.MapPost("/check", async (IIpMonitorService monitor) => Current(await monitor.CheckIpAsync()));

        // Pushed by whichever check sees the change (the background monitor's included) —
        // the browser never polls.
        ip.MapGet("/events", (IIpChangeNotifier notifier, IIpCheckReader reader, CancellationToken cancellation) =>
            TypedResults.ServerSentEvents(StreamChanges(notifier, reader, cancellation)));
    }

    public sealed record CurrentIp(string? IpAddress, DateTime? LastChecked);

    private static CurrentIp Current(IpCheck? check) => new(check?.IpAddress, check?.LastConfirmedAt);

    private static async IAsyncEnumerable<SseItem<CurrentIp>> StreamChanges(
        IIpChangeNotifier notifier, IIpCheckReader reader, [EnumeratorCancellation] CancellationToken cancellation)
    {
        var changes = Channel.CreateBounded<CurrentIp>(new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        void OnChanged(object? sender, IpChangedEventArgs e) => changes.Writer.TryWrite(Current(e.IpCheck));

        // Subscribed before reading the current value, so a change in between isn't lost.
        notifier.IpChanged += OnChanged;
        try
        {
            yield return new SseItem<CurrentIp>(Current(await reader.GetLatestAsync()), CurrentEvent);

            await foreach (var change in changes.Reader.ReadAllAsync(cancellation))
            {
                yield return new SseItem<CurrentIp>(change, ChangedEvent);
            }
        }
        finally
        {
            notifier.IpChanged -= OnChanged;
        }
    }
}
