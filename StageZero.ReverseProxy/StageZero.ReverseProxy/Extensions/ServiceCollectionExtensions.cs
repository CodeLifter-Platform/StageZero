using Microsoft.Extensions.DependencyInjection;
using StageZero.ReverseProxy.Services;

namespace StageZero.ReverseProxy.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddReverseProxyManagement(this IServiceCollection services)
        {
            var configProvider = services.BuildServiceProvider().GetRequiredService<DatabaseProxyConfigProvider>();
            
            services.AddSingleton<DatabaseProxyConfigProvider>(configProvider);
            services.AddSingleton<IProxyConfigurationService>(sp => sp.GetRequiredService<DatabaseProxyConfigProvider>());
            
            services.AddReverseProxy()
                .LoadFromMemory(new List<Yarp.ReverseProxy.Configuration.RouteConfig>(), 
                               new List<Yarp.ReverseProxy.Configuration.ClusterConfig>())
                .AddConfigFilter<ProxyConfigFilter>();

            services.AddScoped<IProxyHostManager, ProxyHostManager>();
            services.AddScoped<ICertificateService, CertificateService>();
            
            services.AddHostedService<CertificateRenewalBackgroundService>();

            return services;
        }
    }

    public class ProxyConfigFilter : IProxyConfigFilter
    {
        public ValueTask<Yarp.ReverseProxy.Configuration.ClusterConfig> ConfigureClusterAsync(
            Yarp.ReverseProxy.Configuration.ClusterConfig cluster, 
            CancellationToken cancel)
        {
            return new ValueTask<Yarp.ReverseProxy.Configuration.ClusterConfig>(cluster);
        }

        public ValueTask<Yarp.ReverseProxy.Configuration.RouteConfig> ConfigureRouteAsync(
            Yarp.ReverseProxy.Configuration.RouteConfig route, 
            Yarp.ReverseProxy.Configuration.ClusterConfig? cluster, 
            CancellationToken cancel)
        {
            return new ValueTask<Yarp.ReverseProxy.Configuration.RouteConfig>(route);
        }
    }
}
