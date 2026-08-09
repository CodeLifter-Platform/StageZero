using System.ComponentModel;
using System.Runtime.CompilerServices;
using StageZero.DataAdapters.TunnelRoutes;
using StageZero.Models;
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
    Task SaveRouteAsync(TunnelRoute route);
    Task ToggleRouteAsync(int id);
    Task DeleteRouteAsync(int id);
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
    private readonly ILogger<TunnelManagementViewModel> _logger;
    private readonly ITunnelRouteReader _routeReader;
    private readonly ITunnelRouteWriter _routeWriter;
    private readonly ITunnelSyncService _syncService;

    private List<TunnelRoute> _routes = new();
    private bool _isLoading;
    private bool _isTunnelConfigured;
    private string? _tunnelName;
    private string? _zoneName;

    public TunnelManagementViewModel(
        ILogger<TunnelManagementViewModel> logger,
        ITunnelRouteReader routeReader,
        ITunnelRouteWriter routeWriter,
        ITunnelSyncService syncService)
    {
        _logger = logger;
        _routeReader = routeReader;
        _routeWriter = routeWriter;
        _syncService = syncService;
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

    public async Task SaveRouteAsync(TunnelRoute route)
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
            await _syncService.SyncRouteAsync(route);
            await LoadRoutesAsync();
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

    public async Task ToggleRouteAsync(int id)
    {
        try
        {
            var route = await _routeReader.GetByIdAsync(id);
            if (route is null) return;

            route.IsEnabled = !route.IsEnabled;
            await _routeWriter.UpdateAsync(route);

            // Disabling drops the ingress rule but keeps the CNAME, so re-enabling
            // does not wait on DNS propagation.
            if (route.IsEnabled)
            {
                await _syncService.SyncRouteAsync(route);
            }
            else
            {
                await _syncService.SyncAllRoutesAsync();
            }

            await LoadRoutesAsync();

            _logger.LogInformation("Tunnel route {DomainName} is now {Status}",
                route.DomainName, route.IsEnabled ? "enabled" : "disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle tunnel route {RouteId}", id);
            throw new TunnelManagementViewModelException(
                $"Could not toggle the route: {ex.Message}", ex);
        }
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
