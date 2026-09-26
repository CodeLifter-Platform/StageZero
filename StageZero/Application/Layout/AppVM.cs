using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lifted.BlazorAuth.Basic.Models;
using Lifted.BlazorAuth.Basic.Services;
using Microsoft.JSInterop;

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
    Task LoadThemePreferenceAsync();
    Task ToggleDarkModeAsync();
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
    private readonly IJSRuntime _js;
    private User? _currentUser;
    private bool _isDarkMode = true; // dark is the canonical CodeLifter theme

    public AppVM(ILogger<AppVM> logger, IAuthService authService, IJSRuntime js)
    {
        _logger = logger;
        _authService = authService;
        _js = js;
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

    // Browser storage is only reachable once the circuit is interactive, so
    // the layout calls this from OnAfterRenderAsync(firstRender).
    public async Task LoadThemePreferenceAsync()
    {
        try
        {
            var stored = await _js.InvokeAsync<string?>("stageZeroTheme.get");
            if (stored is "light" or "dark")
                IsDarkMode = stored == "dark";
        }
        catch (JSException ex)
        {
            _logger.LogWarning(ex, "Could not read the stored theme preference");
        }
    }

    public async Task ToggleDarkModeAsync()
    {
        IsDarkMode = !IsDarkMode;
        _logger.LogDebug("Dark mode toggled to {IsDarkMode}", IsDarkMode);
        try
        {
            await _js.InvokeVoidAsync("stageZeroTheme.set", IsDarkMode ? "dark" : "light");
        }
        catch (JSException ex)
        {
            _logger.LogWarning(ex, "Could not persist the theme preference");
        }
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

