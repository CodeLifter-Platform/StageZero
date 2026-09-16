using StageZero.DataAdapters.TunnelConfigs;
using StageZero.DataAdapters.TunnelRoutes;
using StageZero.Models;
using StageZero.Services.Access;

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

    /// <summary>
    /// Syncs ingress, ensures the hostname's CNAME points at the tunnel, and provisions
    /// Cloudflare Access for it.
    ///
    /// If Access setup fails the route is rolled back rather than left publicly reachable
    /// with no policy, and the call throws.
    /// </summary>
    Task<RouteSyncResult> SyncRouteAsync(TunnelRoute route);

    /// <summary>
    /// Syncs ingress, removes the hostname's CNAME, and tears down its Access application
    /// and policies. Returns what teardown removed and what it deliberately left behind.
    /// </summary>
    Task<AccessTeardownResult> RemoveRouteAsync(TunnelRoute route);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// What one route sync produced. Carries the Access result so a caller can show a freshly
/// minted service token secret, which Cloudflare returns only once.
/// </summary>
public class RouteSyncResult
{
    public AccessProvisionResult? Access { get; set; }
}

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
    private readonly ITunnelRouteWriter _routeWriter;
    private readonly ICloudflareTunnelService _tunnelService;
    private readonly ITunnelTokenProtector _tokenProtector;
    private readonly IAccessProvisioningService _accessProvisioning;

    public TunnelSyncService(
        ILogger<TunnelSyncService> logger,
        ITunnelConfigReader configReader,
        ITunnelRouteReader routeReader,
        ITunnelRouteWriter routeWriter,
        ICloudflareTunnelService tunnelService,
        ITunnelTokenProtector tokenProtector,
        IAccessProvisioningService accessProvisioning)
    {
        _logger = logger;
        _configReader = configReader;
        _routeReader = routeReader;
        _routeWriter = routeWriter;
        _tunnelService = tunnelService;
        _tokenProtector = tokenProtector;
        _accessProvisioning = accessProvisioning;
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

    public async Task<RouteSyncResult> SyncRouteAsync(TunnelRoute route)
    {
        var config = await RequireConfigAsync();

        // Ingress first: if the CNAME resolved before the rule existed, requests
        // would hit the catch-all 404 until the next sync.
        await PushIngressAsync(config);
        await _tunnelService.EnsureCnameAsync(config.ApiToken, config.ZoneId, route.DomainName, config.TunnelId);

        _logger.LogInformation("Synced tunnel route {DomainName} -> {ForwardUrl}",
            route.DomainName, route.ForwardUrl);

        // Access comes last because it needs the hostname to exist, which means a failure
        // here would otherwise leave the hostname live with nothing in front of it.
        var result = new RouteSyncResult();

        try
        {
            result.Access = await _accessProvisioning.ProvisionAsync(config, route);
        }
        catch (Exception ex)
        {
            await RollBackRouteAsync(config, route, ex);
        }

        return result;
    }

    /// <summary>
    /// Undoes the DNS and ingress work for a route whose Access setup failed, so the
    /// hostname is not reachable without a policy, then rethrows with what happened.
    ///
    /// The codebase had no rollback pattern before this, so it is the simplest correct one:
    /// disable the route, re-push ingress without it, and delete its CNAME. Disabling is
    /// what keeps the next unrelated sync from silently republishing the hostname.
    /// </summary>
    private async Task RollBackRouteAsync(ResolvedTunnelConfig config, TunnelRoute route, Exception cause)
    {
        _logger.LogError(cause,
            "Cloudflare Access setup failed for {DomainName}; rolling the route back", route.DomainName);

        try
        {
            route.IsEnabled = false;
            await _routeWriter.UpdateAsync(route);

            await PushIngressAsync(config);
            await _tunnelService.RemoveCnameAsync(config.ApiToken, config.ZoneId, route.DomainName);

            _logger.LogWarning(
                "Rolled back {DomainName}: the route is disabled and its DNS record is removed",
                route.DomainName);

            throw new AccessProvisioningException(
                $"Cloudflare Access setup failed for {route.DomainName}: {cause.Message} "
                + "The route has been disabled and its DNS record removed, so the hostname is not "
                + "publicly reachable without a policy. Fix the Access settings and save again.",
                cause);
        }
        catch (AccessProvisioningException)
        {
            throw;
        }
        catch (Exception rollbackFailure)
        {
            // Both the Access setup and the undo failed. Say so as loudly as possible:
            // the hostname may be serving traffic with no policy in front of it.
            _logger.LogCritical(rollbackFailure,
                "Rollback failed for {DomainName} after Access setup failed. The hostname may still "
                + "be publicly reachable with no Access policy", route.DomainName);

            throw new AccessProvisioningException(
                $"Cloudflare Access setup failed for {route.DomainName} ({cause.Message}) and the "
                + $"rollback also failed ({rollbackFailure.Message}). The hostname may still be "
                + "publicly reachable with no Access policy — remove its DNS record in Cloudflare now.",
                cause);
        }
    }

    public async Task<AccessTeardownResult> RemoveRouteAsync(TunnelRoute route)
    {
        var config = await RequireConfigAsync();

        // Take the hostname down first. Access teardown reports problems rather than
        // throwing, so the ordering means a cleanup failure can never leave a hostname
        // reachable — at worst it leaves an unused application that denies everything.
        await PushIngressAsync(config);
        await _tunnelService.RemoveCnameAsync(config.ApiToken, config.ZoneId, route.DomainName);

        var teardown = await _accessProvisioning.TeardownAsync(config, route);

        _logger.LogInformation("Removed tunnel route {DomainName}", route.DomainName);

        return teardown;
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
