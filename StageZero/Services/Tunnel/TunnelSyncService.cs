using StageZero.DataAdapters.TunnelConfigs;
using StageZero.DataAdapters.TunnelRoutes;
using StageZero.Models;

namespace StageZero.Services.Tunnel;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Pushes the local TunnelRoute table to Cloudflare: ingress rules on the tunnel,
/// plus the proxied CNAME for each hostname. All route mutations go through here so
/// Cloudflare and the database never drift.
/// </summary>
public interface ITunnelSyncService
{
    /// <summary>Resolved tunnel settings with the API token decrypted, or null if setup has not run.</summary>
    Task<ResolvedTunnelConfig?> GetResolvedConfigAsync();

    /// <summary>Pushes all enabled routes as the tunnel's ingress rules.</summary>
    Task SyncAllRoutesAsync();

    /// <summary>Syncs ingress and ensures the hostname's CNAME points at the tunnel.</summary>
    Task SyncRouteAsync(TunnelRoute route);

    /// <summary>Syncs ingress and removes the hostname's CNAME.</summary>
    Task RemoveRouteAsync(TunnelRoute route);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

public class ResolvedTunnelConfig
{
    public string AccountId { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public string? ZoneName { get; set; }
    public string ApiToken { get; set; } = string.Empty;
    public string TunnelId { get; set; } = string.Empty;
    public string? TunnelName { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class TunnelNotConfiguredException : Exception
{
    public TunnelNotConfiguredException(string message) : base(message) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class TunnelSyncService : ITunnelSyncService
{
    private readonly ILogger<TunnelSyncService> _logger;
    private readonly ITunnelConfigReader _configReader;
    private readonly ITunnelRouteReader _routeReader;
    private readonly ICloudflareTunnelService _tunnelService;
    private readonly ITunnelTokenProtector _tokenProtector;

    public TunnelSyncService(
        ILogger<TunnelSyncService> logger,
        ITunnelConfigReader configReader,
        ITunnelRouteReader routeReader,
        ICloudflareTunnelService tunnelService,
        ITunnelTokenProtector tokenProtector)
    {
        _logger = logger;
        _configReader = configReader;
        _routeReader = routeReader;
        _tunnelService = tunnelService;
        _tokenProtector = tokenProtector;
    }

    public async Task<ResolvedTunnelConfig?> GetResolvedConfigAsync()
    {
        var config = await _configReader.GetAsync();
        if (config is null || !config.IsConfigured)
        {
            return null;
        }

        var apiToken = _tokenProtector.Unprotect(config.ProtectedApiToken);
        if (apiToken is null)
        {
            return null;
        }

        return new ResolvedTunnelConfig
        {
            AccountId = config.CloudflareAccountId,
            ZoneId = config.CloudflareZoneId!,
            ZoneName = config.CloudflareZoneName,
            ApiToken = apiToken,
            TunnelId = config.TunnelId!,
            TunnelName = config.TunnelName
        };
    }

    public async Task SyncAllRoutesAsync()
    {
        var config = await RequireConfigAsync();
        await PushIngressAsync(config);
    }

    public async Task SyncRouteAsync(TunnelRoute route)
    {
        var config = await RequireConfigAsync();

        // Ingress first: if the CNAME resolved before the rule existed, requests
        // would hit the catch-all 404 until the next sync.
        await PushIngressAsync(config);
        await _tunnelService.EnsureCnameAsync(config.ApiToken, config.ZoneId, route.DomainName, config.TunnelId);

        _logger.LogInformation("Synced tunnel route {DomainName} -> {ForwardUrl}",
            route.DomainName, route.ForwardUrl);
    }

    public async Task RemoveRouteAsync(TunnelRoute route)
    {
        var config = await RequireConfigAsync();

        await PushIngressAsync(config);
        await _tunnelService.RemoveCnameAsync(config.ApiToken, config.ZoneId, route.DomainName);

        _logger.LogInformation("Removed tunnel route {DomainName}", route.DomainName);
    }

    private async Task PushIngressAsync(ResolvedTunnelConfig config)
    {
        var enabled = await _routeReader.GetEnabledAsync();
        await _tunnelService.SyncIngressAsync(config.ApiToken, config.AccountId, config.TunnelId, enabled);
    }

    private async Task<ResolvedTunnelConfig> RequireConfigAsync()
    {
        var config = await GetResolvedConfigAsync();
        if (config is null)
        {
            throw new TunnelNotConfiguredException(
                "Cloudflare Tunnel is not configured. Complete setup at /tunnel-settings first.");
        }

        return config;
    }
}
