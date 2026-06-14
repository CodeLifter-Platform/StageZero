using Microsoft.Extensions.DependencyInjection;
using StageZero.ReverseProxy.Services;

namespace StageZero.ReverseProxy.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddReverseProxyManagement(this IServiceCollection services)
        {
            // Note: full YARP routing integration (DatabaseProxyConfigProvider) is
            // still disabled — the host app provides its own IProxyConfigurationService.
            // Proxy host management + Let's Encrypt certificate services are enabled.

            services.AddScoped<IProxyHostManager, ProxyHostManager>();

            // Let's Encrypt certificate services. The challenge store must be a
            // singleton so the HTTP-01 endpoint can read responses published by
            // the scoped CertificateService during issuance. The host must also map
            // GET /.well-known/acme-challenge/{token} to serve from IAcmeChallengeStore.
            services.AddSingleton<IAcmeChallengeStore, InMemoryAcmeChallengeStore>();
            services.AddScoped<ICertificateService, CertificateService>();
            services.AddHostedService<CertificateRenewalBackgroundService>();

            return services;
        }
    }
}
