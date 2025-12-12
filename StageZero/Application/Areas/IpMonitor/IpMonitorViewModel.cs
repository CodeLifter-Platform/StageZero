using System.ComponentModel;
using System.Runtime.CompilerServices;
using StageZero.DataAdapters.IpChecks;
using StageZero.Models;
using StageZero.Services.IpMonitoring;

namespace StageZero.Application.Areas.IpMonitor;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IIpMonitorViewModel : INotifyPropertyChanged
{
    string? CurrentIp { get; }
    DateTime? LastChecked { get; }
    bool IsLoading { get; }
    bool IsChecking { get; }
    List<IpCheckGroup> IpGroups { get; }
    Task OnInitializedAsync();
    Task CheckNowAsync();
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class IpMonitorViewModelException : Exception
{
    public IpMonitorViewModelException(string message) : base(message) { }
    public IpMonitorViewModelException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class IpMonitorViewModel : IIpMonitorViewModel
{
    private readonly ILogger<IpMonitorViewModel> _logger;
    private readonly IIpMonitorService _ipMonitorService;
    private readonly IIpCheckReader _ipCheckReader;
    private string? _currentIp;
    private DateTime? _lastChecked;
    private bool _isLoading;
    private bool _isChecking;
    private List<IpCheckGroup> _ipGroups = new();

    public IpMonitorViewModel(
        ILogger<IpMonitorViewModel> logger,
        IIpMonitorService ipMonitorService,
        IIpCheckReader ipCheckReader)
    {
        _logger = logger;
        _ipMonitorService = ipMonitorService;
        _ipCheckReader = ipCheckReader;

        // Subscribe to IP changes
        _ipMonitorService.IpChanged += OnIpChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? CurrentIp
    {
        get => _currentIp;
        private set => SetProperty(ref _currentIp, value);
    }

    public DateTime? LastChecked
    {
        get => _lastChecked;
        private set => SetProperty(ref _lastChecked, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set => SetProperty(ref _isChecking, value);
    }

    public List<IpCheckGroup> IpGroups
    {
        get => _ipGroups;
        private set => SetProperty(ref _ipGroups, value);
    }

    public async Task OnInitializedAsync()
    {
        try
        {
            IsLoading = true;
            _logger.LogDebug("Initializing IpMonitorViewModel");

            await LoadDataAsync();

            _logger.LogInformation("IpMonitorViewModel initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize IpMonitorViewModel");
            throw new IpMonitorViewModelException("Could not initialize IP monitor page", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task CheckNowAsync()
    {
        try
        {
            IsChecking = true;
            _logger.LogDebug("Manual IP check requested");

            var check = await _ipMonitorService.CheckIpAsync();
            CurrentIp = check.IpAddress;
            LastChecked = check.CheckedAt;

            await LoadIpGroupsAsync();

            _logger.LogInformation("Manual IP check completed: {IpAddress}", check.IpAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check IP");
            throw new IpMonitorViewModelException("Could not check IP address", ex);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task LoadDataAsync()
    {
        var latestCheck = await _ipCheckReader.GetLatestAsync();
        CurrentIp = latestCheck?.IpAddress;
        LastChecked = latestCheck?.CheckedAt;

        await LoadIpGroupsAsync();
    }

    private async Task LoadIpGroupsAsync()
    {
        IpGroups = await _ipCheckReader.GetGroupedByIpAsync();
    }

    private void OnIpChanged(object? sender, IpChangedEventArgs e)
    {
        _logger.LogInformation("IP changed event received: {NewIp}", e.NewIp);
        CurrentIp = e.NewIp;
        LastChecked = e.IpCheck.CheckedAt;

        // Reload IP groups
        Task.Run(async () => await LoadIpGroupsAsync());
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

