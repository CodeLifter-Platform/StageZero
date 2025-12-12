using StageZero.DataAdapters.Settings;

namespace StageZero.Services.IpMonitoring;

/// <summary>
/// Background service that periodically checks the public IP address.
/// </summary>
public class IpMonitorBackgroundService : BackgroundService
{
    private readonly ILogger<IpMonitorBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private const string CHECK_INTERVAL_KEY = "IpCheckIntervalSeconds";
    private const int DEFAULT_INTERVAL_SECONDS = 180;

    public IpMonitorBackgroundService(
        ILogger<IpMonitorBackgroundService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IP Monitor Background Service starting");

        try
        {
            // Wait a bit before starting to allow the app to fully initialize
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Create a scope for scoped services
                    using var scope = _serviceProvider.CreateScope();
                    var ipMonitorService = scope.ServiceProvider.GetRequiredService<IIpMonitorService>();
                    var settingsReader = scope.ServiceProvider.GetRequiredService<ISettingsReader>();

                    // Check IP
                    _logger.LogDebug("Running scheduled IP check");
                    await ipMonitorService.CheckIpAsync();

                    // Get the check interval from settings
                    var intervalSeconds = await settingsReader.GetIntValueAsync(
                        CHECK_INTERVAL_KEY,
                        DEFAULT_INTERVAL_SECONDS);

                    _logger.LogDebug("Next IP check in {Seconds} seconds", intervalSeconds);

                    // Wait for the configured interval
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Service is stopping
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in IP monitor background service");

                    // Wait a bit before retrying on error (but catch cancellation)
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Service is stopping during initialization
            _logger.LogInformation("IP Monitor Background Service cancelled during startup");
        }

        _logger.LogInformation("IP Monitor Background Service stopping");
    }
}

