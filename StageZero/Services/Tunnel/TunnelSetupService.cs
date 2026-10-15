using StageZero.DataAdapters.TunnelConfigs;
using StageZero.DataAdapters.TunnelRoutes;
using StageZero.Models;
using StageZero.Services.Dns;

namespace StageZero.Services.Tunnel;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The tunnel setup wizard: verify credentials, pick a zone, create or adopt a tunnel, and
/// hand back the connector token for the host install. Stateless: the token typed on the
/// form comes with every step until a tunnel is saved, then lives encrypted in the database.
/// </summary>
public interface ITunnelSetupService
{
    Task<TunnelConfig?> GetSavedConfigAsync();

    /// <summary>Validates the credentials and lists the zones and tunnels they can see.</summary>
    Task<(List<CloudflareZone> Zones, List<TunnelInfo> Tunnels)> LoadAccountAsync(string apiToken, string accountId);

    Task<TunnelSetupResult> CreateTunnelAsync(string apiToken, string accountId, string zoneId, string tunnelName);

    /// <summary>
    /// Adopts a tunnel already on the account. Whether it needs switching to remote config
    /// is read from Cloudflare, not taken from the caller.
    /// </summary>
    Task<TunnelSetupResult> AdoptTunnelAsync(string apiToken, string accountId, string zoneId, string tunnelId);

    /// <summary>Re-fetches the connector token for the saved tunnel.</summary>
    Task<string> GetConnectorTokenAsync();

    Task ClearConfigurationAsync();
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

/// <param name="WasLocallyManaged">The adopted tunnel used a local config.yml, which must now be retired on the host.</param>
public sealed record TunnelSetupResult(TunnelConfig Config, string ConnectorToken, bool WasLocallyManaged);

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class TunnelSetupService : ITunnelSetupService
{
    private readonly ILogger<TunnelSetupService> _logger;
    private readonly ITunnelConfigReader _configReader;
    private readonly ITunnelConfigWriter _configWriter;
    private readonly ITunnelRouteReader _routeReader;
    private readonly ICloudflareService _cloudflareService;
    private readonly ICloudflareTunnelService _tunnelService;
    private readonly ICloudflareTokenProtector _tokenProtector;
    private readonly ITunnelSyncService _syncService;

    public TunnelSetupService(
        ILogger<TunnelSetupService> logger,
        ITunnelConfigReader configReader,
        ITunnelConfigWriter configWriter,
        ITunnelRouteReader routeReader,
        ICloudflareService cloudflareService,
        ICloudflareTunnelService tunnelService,
        ICloudflareTokenProtector tokenProtector,
        ITunnelSyncService syncService)
    {
        _logger = logger;
        _configReader = configReader;
        _configWriter = configWriter;
        _routeReader = routeReader;
        _cloudflareService = cloudflareService;
        _tunnelService = tunnelService;
        _tokenProtector = tokenProtector;
        _syncService = syncService;
    }

    public Task<TunnelConfig?> GetSavedConfigAsync() => _configReader.GetAsync();

    public async Task<(List<CloudflareZone> Zones, List<TunnelInfo> Tunnels)> LoadAccountAsync(string apiToken, string accountId)
    {
        (apiToken, accountId) = RequireCredentials(apiToken, accountId);
        _logger.LogInformation("Loading Cloudflare zones and tunnels for account {AccountId}", accountId);

        List<CloudflareZone> zones;
        List<TunnelInfo> tunnels;
        try
        {
            // Reuses the DNS provider's zone listing: same token, same endpoint.
            zones = await _cloudflareService.GetZonesAsync(apiToken);
            tunnels = await _tunnelService.ListTunnelsAsync(apiToken, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Cloudflare account details");
            throw new UserFacingException($"Could not reach Cloudflare with those credentials: {ex.Message}", ex);
        }

        if (zones.Count == 0)
        {
            throw new UserFacingException(
                "The token is valid but can't see any zones. Check that it has Zone → Zone → Read.");
        }

        return (zones, tunnels);
    }

    public async Task<TunnelSetupResult> CreateTunnelAsync(string apiToken, string accountId, string zoneId, string tunnelName)
    {
        (apiToken, accountId) = RequireCredentials(apiToken, accountId);
        tunnelName = tunnelName?.Trim() ?? string.Empty;
        if (tunnelName.Length == 0) throw new UserFacingException("Tunnel name is required.");
        var zone = await RequireZoneAsync(apiToken, zoneId);

        try
        {
            var tunnel = await _tunnelService.CreateTunnelAsync(apiToken, accountId, tunnelName);
            return await SaveAndFetchTokenAsync(apiToken, accountId, zone, tunnel, wasLocal: false);
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogError(ex, "Failed to create tunnel {TunnelName}", tunnelName);
            throw new UserFacingException($"Could not create the tunnel: {ex.Message}", ex);
        }
    }

    public async Task<TunnelSetupResult> AdoptTunnelAsync(string apiToken, string accountId, string zoneId, string tunnelId)
    {
        (apiToken, accountId) = RequireCredentials(apiToken, accountId);
        var zone = await RequireZoneAsync(apiToken, zoneId);

        try
        {
            var tunnel = (await _tunnelService.ListTunnelsAsync(apiToken, accountId))
                         .FirstOrDefault(t => t.Id == tunnelId)
                         ?? throw UserFacingException.NotFound("That tunnel isn't on the account any more.");

            // A tunnel created from a local config.yml must be switched to remotely-managed
            // before StageZero can push ingress rules to it.
            var wasLocal = !tunnel.IsRemotelyManaged;
            if (wasLocal)
            {
                await _tunnelService.AdoptTunnelAsync(apiToken, accountId, tunnel.Id);
            }

            return await SaveAndFetchTokenAsync(apiToken, accountId, zone, tunnel, wasLocal);
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogError(ex, "Failed to adopt tunnel {TunnelId}", tunnelId);
            throw new UserFacingException($"Could not adopt the tunnel: {ex.Message}", ex);
        }
    }

    public async Task<string> GetConnectorTokenAsync()
    {
        var config = await _syncService.GetResolvedConfigAsync()
                     ?? throw UserFacingException.Conflict("No usable tunnel configuration is saved. Run setup again.");
        try
        {
            return await _tunnelService.GetConnectorTokenAsync(config.ApiToken, config.AccountId, config.TunnelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh the connector token");
            throw new UserFacingException($"Could not fetch the connector token: {ex.Message}", ex);
        }
    }

    public async Task ClearConfigurationAsync()
    {
        _logger.LogWarning("Clearing saved Cloudflare Tunnel configuration");
        await _configWriter.DeleteAsync();
    }

    private static (string ApiToken, string AccountId) RequireCredentials(string apiToken, string accountId)
    {
        apiToken = apiToken?.Trim() ?? string.Empty;
        accountId = accountId?.Trim() ?? string.Empty;
        if (apiToken.Length == 0) throw new UserFacingException("API token is required.");
        if (accountId.Length == 0) throw new UserFacingException("Account ID is required.");
        return (apiToken, accountId);
    }

    /// <summary>The zone, looked up with the caller's token so its name comes from Cloudflare.</summary>
    private async Task<CloudflareZone> RequireZoneAsync(string apiToken, string zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) throw new UserFacingException("Pick a zone.");
        List<CloudflareZone> zones;
        try
        {
            zones = await _cloudflareService.GetZonesAsync(apiToken);
        }
        catch (Exception ex)
        {
            throw new UserFacingException($"Could not reach Cloudflare with those credentials: {ex.Message}", ex);
        }

        return zones.FirstOrDefault(z => z.Id == zoneId.Trim())
               ?? throw new UserFacingException("That zone isn't visible to this token.");
    }

    private async Task<TunnelSetupResult> SaveAndFetchTokenAsync(
        string apiToken, string accountId, CloudflareZone zone, TunnelInfo tunnel, bool wasLocal)
    {
        var saved = await _configWriter.UpsertAsync(new TunnelConfig
        {
            CloudflareAccountId = accountId,
            CloudflareZoneId = zone.Id,
            CloudflareZoneName = zone.Name,
            ProtectedApiToken = _tokenProtector.Protect(apiToken),
            TunnelId = tunnel.Id,
            TunnelName = tunnel.Name
        });

        var connectorToken = await _tunnelService.GetConnectorTokenAsync(apiToken, accountId, tunnel.Id);

        // Publish existing routes to the newly connected tunnel. Skipped when there are none:
        // an empty push writes a catch-all-only config, which would take down an adopted
        // tunnel before the user has re-created its routes.
        var routes = await _routeReader.GetAllAsync();
        if (routes.Count > 0)
        {
            await _syncService.SyncAllRoutesAsync();
        }

        _logger.LogInformation("Tunnel {TunnelName} ({TunnelId}) configured for zone {ZoneName}",
            tunnel.Name, tunnel.Id, zone.Name);
        return new TunnelSetupResult(saved, connectorToken, wasLocal);
    }
}
