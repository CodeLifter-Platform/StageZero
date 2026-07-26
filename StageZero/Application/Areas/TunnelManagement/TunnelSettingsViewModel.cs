using System.ComponentModel;
using System.Runtime.CompilerServices;
using StageZero.DataAdapters.TunnelConfigs;
using StageZero.DataAdapters.TunnelRoutes;
using StageZero.Models;
using StageZero.Services.Dns;
using StageZero.Services.Tunnel;

namespace StageZero.Application.Areas.TunnelManagement;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Drives the tunnel setup wizard: verify credentials, pick a zone, create or
/// adopt a tunnel, and surface the connector token for the host install.
/// </summary>
public interface ITunnelSettingsViewModel : INotifyPropertyChanged
{
    bool IsLoading { get; }
    bool IsBusy { get; }

    /// <summary>Existing saved configuration, if setup has already been completed.</summary>
    TunnelConfig? SavedConfig { get; }

    List<CloudflareZone> Zones { get; }
    List<TunnelInfo> Tunnels { get; }
    string? ConnectorToken { get; }

    Task OnInitializedAsync();

    /// <summary>Validates the credentials and loads the zones and tunnels they can see.</summary>
    Task LoadAccountAsync(string apiToken, string accountId);

    Task CreateTunnelAsync(string apiToken, string accountId, string zoneId, string zoneName, string tunnelName);
    Task AdoptTunnelAsync(string apiToken, string accountId, string zoneId, string zoneName, TunnelInfo tunnel);

    /// <summary>Re-fetches the connector token for the already-saved tunnel.</summary>
    Task RefreshConnectorTokenAsync();

    Task ClearConfigurationAsync();
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class TunnelSettingsViewModelException : Exception
{
    public TunnelSettingsViewModelException(string message) : base(message) { }
    public TunnelSettingsViewModelException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class TunnelSettingsViewModel : ITunnelSettingsViewModel
{
    private readonly ILogger<TunnelSettingsViewModel> _logger;
    private readonly ITunnelConfigReader _configReader;
    private readonly ITunnelConfigWriter _configWriter;
    private readonly ITunnelRouteReader _routeReader;
    private readonly ICloudflareService _cloudflareService;
    private readonly ICloudflareTunnelService _tunnelService;
    private readonly ITunnelTokenProtector _tokenProtector;
    private readonly ITunnelSyncService _syncService;

    private bool _isLoading;
    private bool _isBusy;
    private TunnelConfig? _savedConfig;
    private List<CloudflareZone> _zones = new();
    private List<TunnelInfo> _tunnels = new();
    private string? _connectorToken;

    public TunnelSettingsViewModel(
        ILogger<TunnelSettingsViewModel> logger,
        ITunnelConfigReader configReader,
        ITunnelConfigWriter configWriter,
        ITunnelRouteReader routeReader,
        ICloudflareService cloudflareService,
        ICloudflareTunnelService tunnelService,
        ITunnelTokenProtector tokenProtector,
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

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public TunnelConfig? SavedConfig
    {
        get => _savedConfig;
        private set => SetProperty(ref _savedConfig, value);
    }

    public List<CloudflareZone> Zones
    {
        get => _zones;
        private set => SetProperty(ref _zones, value);
    }

    public List<TunnelInfo> Tunnels
    {
        get => _tunnels;
        private set => SetProperty(ref _tunnels, value);
    }

    public string? ConnectorToken
    {
        get => _connectorToken;
        private set => SetProperty(ref _connectorToken, value);
    }

    public async Task OnInitializedAsync()
    {
        try
        {
            IsLoading = true;
            SavedConfig = await _configReader.GetAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize TunnelSettingsViewModel");
            throw new TunnelSettingsViewModelException("Could not load tunnel settings", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadAccountAsync(string apiToken, string accountId)
    {
        try
        {
            IsBusy = true;
            _logger.LogInformation("Loading Cloudflare zones and tunnels for account {AccountId}", accountId);

            // Reuses the DNS provider's zone listing — same token, same endpoint.
            Zones = await _cloudflareService.GetZonesAsync(apiToken);
            Tunnels = await _tunnelService.ListTunnelsAsync(apiToken, accountId);

            if (Zones.Count == 0)
            {
                throw new TunnelSettingsViewModelException(
                    "The token is valid but can't see any zones. Check that it has Zone → Zone → Read.");
            }
        }
        catch (TunnelSettingsViewModelException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Cloudflare account details");
            throw new TunnelSettingsViewModelException(
                $"Could not reach Cloudflare with those credentials: {ex.Message}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CreateTunnelAsync(
        string apiToken,
        string accountId,
        string zoneId,
        string zoneName,
        string tunnelName)
    {
        try
        {
            IsBusy = true;

            var tunnel = await _tunnelService.CreateTunnelAsync(apiToken, accountId, tunnelName);
            await SaveAndFetchTokenAsync(apiToken, accountId, zoneId, zoneName, tunnel);
        }
        catch (Exception ex) when (ex is not TunnelSettingsViewModelException)
        {
            _logger.LogError(ex, "Failed to create tunnel {TunnelName}", tunnelName);
            throw new TunnelSettingsViewModelException(
                $"Could not create the tunnel: {ex.Message}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AdoptTunnelAsync(
        string apiToken,
        string accountId,
        string zoneId,
        string zoneName,
        TunnelInfo tunnel)
    {
        try
        {
            IsBusy = true;

            // A tunnel created from a local config.yml must be switched to
            // remotely-managed before StageZero can push ingress rules to it.
            if (!tunnel.IsRemotelyManaged)
            {
                await _tunnelService.AdoptTunnelAsync(apiToken, accountId, tunnel.Id);
            }

            await SaveAndFetchTokenAsync(apiToken, accountId, zoneId, zoneName, tunnel);
        }
        catch (Exception ex) when (ex is not TunnelSettingsViewModelException)
        {
            _logger.LogError(ex, "Failed to adopt tunnel {TunnelId}", tunnel.Id);
            throw new TunnelSettingsViewModelException(
                $"Could not adopt the tunnel: {ex.Message}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshConnectorTokenAsync()
    {
        try
        {
            IsBusy = true;

            var config = await _syncService.GetResolvedConfigAsync();
            if (config is null)
            {
                throw new TunnelSettingsViewModelException(
                    "No usable tunnel configuration is saved. Run setup again.");
            }

            ConnectorToken = await _tunnelService.GetConnectorTokenAsync(
                config.ApiToken, config.AccountId, config.TunnelId);
        }
        catch (Exception ex) when (ex is not TunnelSettingsViewModelException)
        {
            _logger.LogError(ex, "Failed to refresh the connector token");
            throw new TunnelSettingsViewModelException(
                $"Could not fetch the connector token: {ex.Message}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ClearConfigurationAsync()
    {
        try
        {
            IsBusy = true;
            _logger.LogWarning("Clearing saved Cloudflare Tunnel configuration");

            await _configWriter.DeleteAsync();
            SavedConfig = null;
            ConnectorToken = null;
            Zones = new();
            Tunnels = new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear tunnel configuration");
            throw new TunnelSettingsViewModelException("Could not clear the configuration", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAndFetchTokenAsync(
        string apiToken,
        string accountId,
        string zoneId,
        string zoneName,
        TunnelInfo tunnel)
    {
        SavedConfig = await _configWriter.UpsertAsync(new TunnelConfig
        {
            CloudflareAccountId = accountId,
            CloudflareZoneId = zoneId,
            CloudflareZoneName = zoneName,
            ProtectedApiToken = _tokenProtector.Protect(apiToken),
            TunnelId = tunnel.Id,
            TunnelName = tunnel.Name
        });

        ConnectorToken = await _tunnelService.GetConnectorTokenAsync(apiToken, accountId, tunnel.Id);

        // Publish existing routes to the newly-connected tunnel. Skipped when there
        // are none: an empty push writes a catch-all-only config, which would take
        // down an adopted tunnel before the user has re-created its routes.
        var routes = await _routeReader.GetAllAsync();
        if (routes.Count > 0)
        {
            await _syncService.SyncAllRoutesAsync();
        }

        _logger.LogInformation("Tunnel {TunnelName} ({TunnelId}) configured for zone {ZoneName}",
            tunnel.Name, tunnel.Id, zoneName);
    }

    // --- Property Change Support ---

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
