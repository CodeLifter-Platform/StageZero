using StageZero.Services.Dns;

namespace StageZero.Services.IpMonitoring;

/// <summary>
/// Service that listens to IP changes and triggers DNS updates.
/// </summary>
public class IpChangeHandlerService : IHostedService
{
    private readonly ILogger<IpChangeHandlerService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public IpChangeHandlerService(
        ILogger<IpChangeHandlerService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IP Change Handler Service starting");

        // Subscribe to IP change events
        // We need to do this in a scope to get the scoped service
        Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var ipMonitorService = scope.ServiceProvider.GetRequiredService<IIpMonitorService>();
            
            ipMonitorService.IpChanged += async (sender, args) =>
            {
                _logger.LogInformation("IP changed from {OldIp} to {NewIp}, triggering DNS updates",
                    args.OldIp ?? "none", args.NewIp);

                try
                {
                    using var updateScope = _serviceProvider.CreateScope();
                    var dnsUpdateService = updateScope.ServiceProvider.GetRequiredService<IDnsUpdateService>();
                    await dnsUpdateService.UpdateAllRecordsAsync(args.NewIp);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling IP change");
                }
            };
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IP Change Handler Service stopping");
        return Task.CompletedTask;
    }
}

