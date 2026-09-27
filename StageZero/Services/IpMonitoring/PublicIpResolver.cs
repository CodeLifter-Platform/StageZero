using System.Net;
using System.Net.Sockets;

namespace StageZero.Services.IpMonitoring;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Finds the public IPv4 address by asking several independent services and agreeing on the
/// answer. A single source can be down, rate-limited, or replaced by a captive portal's HTML
/// page; acting on one bad answer would repoint every DNS record at it.
/// </summary>
public interface IPublicIpResolver
{
    /// <summary>
    /// The agreed address. <paramref name="lastKnown"/> lets a lone answer through when it
    /// only confirms nothing changed; a change always needs two sources agreeing.
    /// </summary>
    /// <exception cref="PublicIpUnresolvedException">The sources don't agree well enough to act on.</exception>
    Task<string> ResolveAsync(string? lastKnown, CancellationToken cancellationToken = default);
}

public sealed class PublicIpUnresolvedException : Exception
{
    public PublicIpUnresolvedException(string message) : base(message) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public sealed class PublicIpResolver : IPublicIpResolver
{
    public const string HttpClientName = "public-ip";

    /// <summary>Independent operators, so one outage or one bad answer can't decide alone.</summary>
    public static readonly IReadOnlyList<(string Name, string Url)> Sources =
    [
        ("Cloudflare", "https://1.1.1.1/cdn-cgi/trace"),
        ("ipify", "https://api.ipify.org"),
        ("AWS", "https://checkip.amazonaws.com")
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PublicIpResolver> _logger;

    public PublicIpResolver(IHttpClientFactory httpClientFactory, ILogger<PublicIpResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> ResolveAsync(string? lastKnown, CancellationToken cancellationToken = default)
    {
        var answers = await Task.WhenAll(Sources.Select(source => AskAsync(source.Name, source.Url, cancellationToken)));
        var valid = answers.OfType<string>().ToList();

        var agreed = valid
            .GroupBy(ip => ip)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault(group => group.Count() >= 2);
        if (agreed is not null)
        {
            if (agreed.Count() < valid.Count)
            {
                _logger.LogWarning("IP sources disagree ({Answers}); going with {Ip}", string.Join(", ", valid), agreed.Key);
            }

            return agreed.Key;
        }

        // One answer can confirm that nothing changed, but never move DNS on its own.
        if (valid.Count == 1 && valid[0] == lastKnown)
        {
            return valid[0];
        }

        throw new PublicIpUnresolvedException(valid.Count == 0
            ? "No IP source gave a usable answer"
            : $"IP sources don't agree on a change ({string.Join(", ", valid)}); not acting on it");
    }

    private async Task<string?> AskAsync(string name, string url, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var body = await client.GetStringAsync(url, cancellationToken);
            var ip = Parse(body);
            if (ip is null)
            {
                _logger.LogWarning("IP source {Source} gave an unusable answer: {Answer}", name, Truncate(body));
            }

            return ip;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Any failure of one source is just a missing answer; the others still count.
            _logger.LogWarning("IP source {Source} failed: {Error}", name, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// The address in a source's reply — plain text, or Cloudflare's trace ("ip=…") — if it
    /// is a public IPv4 address. Anything else (an HTML page, a private address) is null.
    /// </summary>
    public static string? Parse(string body)
    {
        var text = body.Trim();
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("ip=", StringComparison.Ordinal))
            {
                text = line[3..].Trim();
                break;
            }
        }

        return IPAddress.TryParse(text, out var ip)
            && ip.AddressFamily == AddressFamily.InterNetwork
            && text.Count(c => c == '.') == 3 // IPAddress.TryParse also accepts "1" or "1.2"
            && IsPublic(ip)
            ? ip.ToString()
            : null;
    }

    private static bool IsPublic(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return !(b[0] == 0                                  // this network
            || b[0] == 10                                   // private
            || b[0] == 127                                  // loopback
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // carrier-grade NAT
            || (b[0] == 169 && b[1] == 254)                 // link-local
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // private
            || (b[0] == 192 && b[1] == 168)                 // private
            || b[0] >= 224);                                // multicast, reserved, broadcast
    }

    private static string Truncate(string value) => value.Length <= 80 ? value : value[..80] + "…";
}
