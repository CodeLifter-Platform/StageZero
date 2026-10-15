using StageZero.Models;
using StageZero.Services.Tunnel;

namespace StageZero.Tests.Fakes;

/// <summary>A Cloudflare account's tunnels and CNAMEs, in memory.</summary>
public class FakeCloudflareTunnelService : ICloudflareTunnelService
{
    public const string ConnectorToken = "eyJhIjoiY29ubmVjdG9yLXRva2VuIn0";

    public List<TunnelInfo> Tunnels { get; } = new();
    public List<string> Adopted { get; } = new();
    public List<string> Cnames { get; } = new();
    public List<string> LastIngress { get; private set; } = new();

    public Task<List<TunnelInfo>> ListTunnelsAsync(string apiToken, string accountId) => Task.FromResult(Tunnels.ToList());

    public Task<TunnelInfo> CreateTunnelAsync(string apiToken, string accountId, string name)
    {
        var tunnel = new TunnelInfo { Id = $"tunnel-{Tunnels.Count + 1}", Name = name, ConfigSrc = "cloudflare" };
        Tunnels.Add(tunnel);
        return Task.FromResult(tunnel);
    }

    public Task AdoptTunnelAsync(string apiToken, string accountId, string tunnelId)
    {
        Adopted.Add(tunnelId);
        return Task.CompletedTask;
    }

    public Task<string> GetConnectorTokenAsync(string apiToken, string accountId, string tunnelId) => Task.FromResult(ConnectorToken);

    public Task SyncIngressAsync(string apiToken, string accountId, string tunnelId, IEnumerable<TunnelRoute> enabledRoutes)
    {
        LastIngress = enabledRoutes.Select(r => r.DomainName).ToList();
        return Task.CompletedTask;
    }

    public Task EnsureCnameAsync(string apiToken, string zoneId, string hostname, string tunnelId)
    {
        if (!Cnames.Contains(hostname)) Cnames.Add(hostname);
        return Task.CompletedTask;
    }

    public Task RemoveCnameAsync(string apiToken, string zoneId, string hostname)
    {
        Cnames.Remove(hostname);
        return Task.CompletedTask;
    }
}
