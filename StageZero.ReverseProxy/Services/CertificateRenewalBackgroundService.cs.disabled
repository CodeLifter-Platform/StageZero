using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StageZero.ReverseProxy.Services
{
    public class CertificateRenewalBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CertificateRenewalBackgroundService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

        public CertificateRenewalBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<CertificateRenewalBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Certificate Renewal Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Checking for certificates that need renewal");

                    using var scope = _serviceProvider.CreateScope();
                    var certificateService = scope.ServiceProvider.GetRequiredService<ICertificateService>();

                    await certificateService.RenewExpiringCertificatesAsync();

                    _logger.LogInformation("Certificate renewal check completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during certificate renewal check");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Certificate Renewal Background Service stopped");
        }
    }
}
