using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StageZero.Application.Areas.Home;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IHomeViewModel : INotifyPropertyChanged
{
    string WelcomeMessage { get; }
    string AppDescription { get; }
    bool IsLoading { get; }
    Task OnInitializedAsync();
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class HomeViewModelException : Exception
{
    public HomeViewModelException(string message) : base(message) { }
    public HomeViewModelException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class HomeViewModel : IHomeViewModel
{
    private readonly ILogger<HomeViewModel> _logger;
    private string _welcomeMessage = string.Empty;
    private string _appDescription = string.Empty;
    private bool _isLoading;

    public HomeViewModel(ILogger<HomeViewModel> logger)
    {
        _logger = logger;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string WelcomeMessage
    {
        get => _welcomeMessage;
        private set => SetProperty(ref _welcomeMessage, value);
    }

    public string AppDescription
    {
        get => _appDescription;
        private set => SetProperty(ref _appDescription, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public async Task OnInitializedAsync()
    {
        try
        {
            IsLoading = true;
            _logger.LogDebug("Initializing HomeViewModel");

            // Simulate loading data
            await Task.Delay(100);

            WelcomeMessage = "Welcome to StageZero";
            AppDescription = "A Dynamic DNS tool that updates changes to DNS services. Keep your domains pointed at the right IP addresses, automatically.";

            _logger.LogInformation("HomeViewModel initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize HomeViewModel");
            throw new HomeViewModelException("Could not initialize home page", ex);
        }
        finally
        {
            IsLoading = false;
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

