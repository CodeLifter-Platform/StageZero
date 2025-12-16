using System.ComponentModel;
using System.Runtime.CompilerServices;
using StageZero.Models;
using StageZero.Services.Auth;

namespace StageZero.Application.Layout;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IAppVM : INotifyPropertyChanged
{
    User? CurrentUser { get; }
    bool IsAuthenticated { get; }
    bool IsDarkMode { get; set; }
    Task OnInitializedAsync();
    Task RefreshCurrentUserAsync();
    void ToggleDarkMode();
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class AppVMException : Exception
{
    public AppVMException(string message) : base(message) { }
    public AppVMException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class AppVM : IAppVM
{
    private readonly ILogger<AppVM> _logger;
    private readonly IAuthService _authService;
    private User? _currentUser;
    private bool _isDarkMode = false;

    public AppVM(ILogger<AppVM> logger, IAuthService authService)
    {
        _logger = logger;
        _authService = authService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // --- App-wide state ---

    public User? CurrentUser
    {
        get => _currentUser;
        private set => SetProperty(ref _currentUser, value);
    }

    public bool IsAuthenticated => CurrentUser != null;

    // --- Layout state ---

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set => SetProperty(ref _isDarkMode, value);
    }

    // --- Commands ---

    public async Task OnInitializedAsync()
    {
        try
        {
            _logger.LogDebug("Initializing AppVM");
            CurrentUser = await _authService.GetCurrentUserAsync();
            _logger.LogInformation("AppVM initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize AppVM");
            throw new AppVMException("Could not initialize application", ex);
        }
    }

    public async Task RefreshCurrentUserAsync()
    {
        try
        {
            _logger.LogDebug("Refreshing current user in AppVM");
            CurrentUser = await _authService.GetCurrentUserAsync();
            _logger.LogDebug("Current user refreshed: {IsAuthenticated}", IsAuthenticated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh current user");
            throw new AppVMException("Could not refresh current user", ex);
        }
    }

    public void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        _logger.LogDebug("Dark mode toggled to {IsDarkMode}", IsDarkMode);
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

