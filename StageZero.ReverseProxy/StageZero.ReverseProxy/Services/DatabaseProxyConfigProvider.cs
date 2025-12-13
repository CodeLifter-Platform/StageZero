using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using StageZero.ReverseProxy.Models;

namespace StageZero.ReverseProxy.Services
{
    public interface IProxyConfigurationService
    {
        Task ReloadConfigurationAsync();
    }

    public class DatabaseProxyConfigProvider : IProxyConfigProvider, IProxyConfigurationService
    {
        private readonly DbContext _dbContext;
        private volatile InMemoryConfigProvider _configProvider;
        private readonly object _lock = new object();

        public DatabaseProxyConfigProvider(DbContext dbContext)
        {
            _dbContext = dbContext;
            _configProvider = new InMemoryConfigProvider(new List<RouteConfig>(), new List<ClusterConfig>());
        }

        public IProxyConfig GetConfig()
        {
            return _configProvider.GetConfig();
        }

        public async Task ReloadConfigurationAsync()
        {
            var proxyHosts = await _dbContext.Set<ProxyHost>()
                .Where(p => p.IsEnabled)
                .ToListAsync();

            var routes = new List<RouteConfig>();
            var clusters = new List<ClusterConfig>();

            foreach (var host in proxyHosts)
            {
                var routeId = $"route_{host.Id}";
                var clusterId = $"cluster_{host.Id}";

                var route = new RouteConfig
                {
                    RouteId = routeId,
                    ClusterId = clusterId,
                    Match = new RouteMatch
                    {
                        Hosts = new[] { host.DomainName }
                    }
                };

                var metadata = new Dictionary<string, string>();
                
                if (host.WebSocketsSupport)
                {
                    metadata["WebSocketsSupport"] = "true";
                }

                var cluster = new ClusterConfig
                {
                    ClusterId = clusterId,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        {
                            $"destination_{host.Id}",
                            new DestinationConfig
                            {
                                Address = $"{host.ForwardScheme}://{host.ForwardHost}:{host.ForwardPort}"
                            }
                        }
                    },
                    Metadata = metadata
                };

                routes.Add(route);
                clusters.Add(cluster);
            }

            lock (_lock)
            {
                var oldConfig = _configProvider;
                _configProvider = new InMemoryConfigProvider(routes, clusters);
                oldConfig.SignalChange();
            }
        }

        private class InMemoryConfigProvider : IProxyConfig
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public InMemoryConfigProvider(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
            {
                Routes = routes;
                Clusters = clusters;
                ChangeToken = new CancellationChangeToken(_cts.Token);
            }

            public IReadOnlyList<RouteConfig> Routes { get; }
            public IReadOnlyList<ClusterConfig> Clusters { get; }
            public IChangeToken ChangeToken { get; }

            internal void SignalChange()
            {
                _cts.Cancel();
            }
        }
    }
}
