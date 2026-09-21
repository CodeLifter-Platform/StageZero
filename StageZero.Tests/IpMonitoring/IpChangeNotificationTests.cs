using Microsoft.Extensions.DependencyInjection;
using StageZero.Services.IpMonitoring;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.IpMonitoring;

/// <summary>
/// A change found by one check — the background monitor's, in its own scope — reaches
/// listeners everywhere else. Before, it was raised on a scoped instance nobody else held.
/// </summary>
[Collection(AppCollection.Name)]
public class IpChangeNotificationTests
{
    [Fact]
    public async Task A_change_seen_in_one_scope_reaches_a_listener_in_another()
    {
        await using var app = new StageZeroApp();
        var heard = new List<IpChangedEventArgs>();

        using (var listenerScope = app.Services.CreateScope())
        {
            listenerScope.ServiceProvider.GetRequiredService<IIpChangeNotifier>().IpChanged += (_, e) => heard.Add(e);

            app.Http.OnGet("https://api.ipify.org", "203.0.113.1");
            await CheckInNewScopeAsync(app);
            app.Http.OnGet("https://api.ipify.org", "203.0.113.2");
            await CheckInNewScopeAsync(app);
            await CheckInNewScopeAsync(app); // unchanged: no news
        }

        Assert.Collection(heard,
            first => Assert.Equal((null, "203.0.113.1"), (first.OldIp, first.NewIp)),
            second => Assert.Equal(("203.0.113.1", "203.0.113.2"), (second.OldIp, second.NewIp)));
    }

    [Fact]
    public async Task One_failing_listener_does_not_stop_the_others_or_the_check()
    {
        await using var app = new StageZeroApp();
        var notifier = app.Services.GetRequiredService<IIpChangeNotifier>();
        var heard = 0;
        notifier.IpChanged += (_, _) => throw new InvalidOperationException("listener bug");
        notifier.IpChanged += (_, _) => heard++;
        app.Http.OnGet("https://api.ipify.org", "203.0.113.1");

        var check = await CheckInNewScopeAsync(app);

        Assert.Equal("203.0.113.1", check.IpAddress);
        Assert.Equal(1, heard);
    }

    private static async Task<StageZero.Models.IpCheck> CheckInNewScopeAsync(StageZeroApp app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IIpMonitorService>().CheckIpAsync();
    }
}
