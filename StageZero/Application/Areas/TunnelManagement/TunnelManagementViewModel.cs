using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lifted.BlazorAuth.Basic.DataAdapters;
using StageZero.DataAdapters.Settings;
using StageZero.DataAdapters.TunnelRoutes;
using StageZero.Models;
using StageZero.Services.Access;
using StageZero.Services.Tunnel;

namespace StageZero.Application.Areas.TunnelManagement;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface ITunnelManagementViewModel : INotifyPropertyChanged
{
    List<TunnelRoute> Routes { get; }
    bool IsLoading { get; }

    /// <summary>False until the setup wizard has stored an account, zone and tunnel.</summary>
    bool IsTunnelConfigured { get; }

    string? TunnelName { get; }
    string? ZoneName { get; }

    Task OnInitializedAsync();
    Task<TunnelRoute?> GetRouteAsync(int id);

    /// <summary>
    /// Builds the access section a brand new hostname starts with: identity mode allowing
    /// the configured owner email. Opting out is a deliberate change from this default.
    /// </summary>
    Task<RouteAccessSettings> BuildDefaultAccessAsync();

    /// <summary>
    /// Saves the route and pushes it to Cloudflare. The result carries a newly minted
    /// service token when one was created — the only time its secret is available.
    /// </summary>
    Task<RouteSyncResult> SaveRouteAsync(TunnelRoute route);

    /// <summary>Flips the route's enabled flag. Returns a sync result when re-enabling.</summary>
    Task<RouteSyncResult?> ToggleRouteAsync(int id);

    Task DeleteRouteAsync(int id);

    /// <summary>Live Access configuration for a route, for the access status page.</summary>
    Task<AccessStatus> GetAccessStatusAsync(int id);

    /// <summary>Identity providers configured on the Cloudflare account.</summary>
    Task<List<AccessIdentityProvider>> GetIdentityProvidersAsync();

    /// <summary>Service tokens already on the Cloudflare account, for attaching an existing one.</summary>
    Task<List<AccessServiceTokenInfo>> GetServiceTokensAsync();
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class TunnelManagementViewModelException : Exception
{
    public TunnelManagementViewModelException(string message) : base(message) { }
    public TunnelManagementViewModelException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class TunnelManagementViewModel : ITunnelManagementViewModel
{
    /// <summary>Settings key overriding which email new hostnames are opened up to.</summary>
    public const string OwnerEmailSettingKey = "Access.OwnerEmail";

    private readonly ILogger<TunnelManagementViewModel> _logger;
    private readonly ITunnelRouteReader _routeReader;
    private readonly ITunnelRouteWriter _routeWriter;
    private readonly ITunnelSyncService _syncService;
    private readonly IAccessProvisioningService _accessProvisioning;
    private readonly ISettingsReader _settingsReader;
    private readonly IUserReader _userReader;

    private List<TunnelRoute> _routes = new();
    private bool _isLoading;
    private bool _isTunnelConfigured;
    private string? _tunnelName;
    private string? _zoneName;

    public TunnelManagementViewModel(
        ILogger<TunnelManagementViewModel> logger,
        ITunnelRouteReader routeReader,
        ITunnelRouteWriter routeWriter,
        ITunnelSyncService syncService,
        IAccessProvisioningService accessProvisioning,
        ISettingsReader settingsReader,
        IUserReader userReader)
    {
        _logger = logger;
        _routeReader = routeReader;
        _routeWriter = routeWriter;
        _syncService = syncService;
        _accessProvisioning = accessProvisioning;
        _settingsReader = settingsReader;
        _userReader = userReader;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<TunnelRoute> Routes
    {
        get => _routes;
        private set => SetProperty(ref _routes, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsTunnelConfigured
    {
        get => _isTunnelConfigured;
        private set => SetProperty(ref _isTunnelConfigured, value);
    }

    public string? TunnelName
    {
        get => _tunnelName;
        private set => SetProperty(ref _tunnelName, value);
    }

    public string? ZoneName
    {
        get => _zoneName;
        private set => SetProperty(ref _zoneName, value);
    }

    public async Task OnInitializedAsync()
    {
        try
        {
            IsLoading = true;
            _logger.LogDebug("Initializing TunnelManagementViewModel");

            var config = await _syncService.GetResolvedConfigAsync();
            IsTunnelConfigured = config is not null;
            TunnelName = config?.TunnelName;
            ZoneName = config?.ZoneName;

            await LoadRoutesAsync();

            _logger.LogInformation("TunnelManagementViewModel initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize TunnelManagementViewModel");
            throw new TunnelManagementViewModelException("Could not initialize tunnel routes page", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<TunnelRoute?> GetRouteAsync(int id)
    {
        return await _routeReader.GetByIdAsync(id);
    }

    public async Task<RouteAccessSettings> BuildDefaultAccessAsync()
    {
        var settings = new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            SessionDuration = RouteAccessSettings.DefaultSessionDuration
        };

        var ownerEmail = await ResolveOwnerEmailAsync();
        if (!string.IsNullOrWhiteSpace(ownerEmail))
        {
            settings.AllowedEmails = ownerEmail;
        }

        return settings;
    }

    /// <summary>
    /// The email new hostnames are opened up to. An explicit Access.OwnerEmail setting wins;
    /// otherwise the first account created during setup is treated as the owner.
    /// </summary>
    private async Task<string?> ResolveOwnerEmailAsync()
    {
        var configured = await _settingsReader.GetValueAsync(OwnerEmailSettingKey);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().ToLowerInvariant();
        }

        var users = await _userReader.GetAllAsync();
        var owner = users.OrderBy(u => u.Id).FirstOrDefault();

        return owner?.Email.Trim().ToLowerInvariant();
    }

    public async Task<RouteSyncResult> SaveRouteAsync(TunnelRoute route)
    {
        try
        {
            route.DomainName = route.DomainName.Trim().ToLowerInvariant();

            var duplicate = await _routeReader.GetByDomainAsync(route.DomainName);
            if (duplicate is not null && duplicate.Id != route.Id)
            {
                throw new TunnelManagementViewModelException(
                    $"A route for '{route.DomainName}' already exists.");
            }

            // Catch bad Access settings here rather than after the DNS record exists, so a
            // typo in an allowed email never costs a rollback.
            var accessErrors = route.Access.Validate();
            if (accessErrors.Count > 0)
            {
                throw new TunnelManagementViewModelException(string.Join(" ", accessErrors));
            }

            if (route.Id == 0)
            {
                _logger.LogInformation("Creating tunnel route {DomainName}", route.DomainName);
                await _routeWriter.InsertAsync(route);
            }
            else
            {
                _logger.LogInformation("Updating tunnel route {DomainName}", route.DomainName);
                await _routeWriter.UpdateAsync(route);
            }

            // Push to Cloudflare after the write so a failed sync leaves a saved
            // route the user can retry, rather than losing their input.
            var result = await _syncService.SyncRouteAsync(route);
            await LoadRoutesAsync();

            return result;
        }
        catch (TunnelManagementViewModelException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save tunnel route {DomainName}", route.DomainName);
            throw new TunnelManagementViewModelException(
                $"Could not save the route: {ex.Message}", ex);
        }
    }

    public async Task<RouteSyncResult?> ToggleRouteAsync(int id)
    {
        try
        {
            var route = await _routeReader.GetByIdAsync(id);
            if (route is null) return null;

            route.IsEnabled = !route.IsEnabled;
            await _routeWriter.UpdateAsync(route);

            // Disabling drops the ingress rule but keeps the CNAME, so re-enabling
            // does not wait on DNS propagation.
            RouteSyncResult? result = null;
            if (route.IsEnabled)
            {
                // Re-enabling re-provisions Access, which can mint a pending service token.
                // The result is returned so the caller can show the secret once.
                result = await _syncService.SyncRouteAsync(route);
            }
            else
            {
                await _syncService.SyncAllRoutesAsync();
            }

            await LoadRoutesAsync();

            _logger.LogInformation("Tunnel route {DomainName} is now {Status}",
                route.DomainName, route.IsEnabled ? "enabled" : "disabled");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle tunnel route {RouteId}", id);
            throw new TunnelManagementViewModelException(
                $"Could not toggle the route: {ex.Message}", ex);
        }
    }

    public async Task<AccessStatus> GetAccessStatusAsync(int id)
    {
        try
        {
            var route = await _routeReader.GetByIdAsync(id)
                        ?? throw new TunnelManagementViewModelException("That route no longer exists.");

            var config = await RequireConfigAsync();
            return await _accessProvisioning.GetStatusAsync(config, route);
        }
        catch (TunnelManagementViewModelException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read Access status for route {RouteId}", id);
            throw new TunnelManagementViewModelException(
                $"Could not read the Access status: {ex.Message}", ex);
        }
    }

    public async Task<List<AccessIdentityProvider>> GetIdentityProvidersAsync()
    {
        try
        {
            var config = await _syncService.GetResolvedConfigAsync();
            if (config is null)
            {
                return new List<AccessIdentityProvider>();
            }

            return await _accessProvisioning.ListIdentityProvidersAsync(config);
        }
        catch (Exception ex)
        {
            // The picker is a convenience: leaving it empty means "every account IdP",
            // which is exactly what Cloudflare does when none is named.
            _logger.LogWarning(ex, "Could not list Cloudflare Access identity providers");
            return new List<AccessIdentityProvider>();
        }
    }

    public async Task<List<AccessServiceTokenInfo>> GetServiceTokensAsync()
    {
        try
        {
            var config = await _syncService.GetResolvedConfigAsync();
            if (config is null)
            {
                return new List<AccessServiceTokenInfo>();
            }

            return await _accessProvisioning.ListServiceTokensAsync(config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list Cloudflare Access service tokens");
            return new List<AccessServiceTokenInfo>();
        }
    }

    private async Task<ResolvedTunnelConfig> RequireConfigAsync()
    {
        return await _syncService.GetResolvedConfigAsync()
               ?? throw new TunnelManagementViewModelException(
                   "Cloudflare Tunnel is not configured. Complete setup at /tunnel-settings first.");
    }

    public async Task DeleteRouteAsync(int id)
    {
        try
        {
            var route = await _routeReader.GetByIdAsync(id);
            if (route is null) return;

            _logger.LogInformation("Deleting tunnel route {DomainName}", route.DomainName);

            await _routeWriter.DeleteAsync(route);
            await _syncService.RemoveRouteAsync(route);
            await LoadRoutesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete tunnel route {RouteId}", id);
            throw new TunnelManagementViewModelException(
                $"Could not delete the route: {ex.Message}", ex);
        }
    }

    private async Task LoadRoutesAsync()
    {
        Routes = await _routeReader.GetAllAsync();
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
