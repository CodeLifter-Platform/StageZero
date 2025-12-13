using Microsoft.Extensions.DependencyInjection;
using StageZero.ReverseProxy.Services;

namespace StageZero.ReverseProxy.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddReverseProxyManagement(this IServiceCollection services)
        {
            // Note: YARP integration is disabled for now - will be added when needed
            // For now, we just register the basic services for proxy host management

            services.AddScoped<IProxyHostManager, ProxyHostManager>();
            // services.AddScoped<ICertificateService, CertificateService>();
            // services.AddHostedService<CertificateRenewalBackgroundService>();

            return services;
        }
    }
}
