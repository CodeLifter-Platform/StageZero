using Microsoft.EntityFrameworkCore;
using StageZero.ReverseProxy.Models;

namespace StageZero.ReverseProxy.Services
{
    public interface IProxyHostManager
    {
        Task<List<ProxyHost>> GetAllAsync();
        Task<ProxyHost?> GetByIdAsync(int id);
        Task<ProxyHost?> GetByDomainAsync(string domain);
        Task<ProxyHost> CreateAsync(ProxyHost proxyHost);
        Task<ProxyHost> UpdateAsync(ProxyHost proxyHost);
        Task<bool> DeleteAsync(int id);
        Task<bool> ToggleEnabledAsync(int id);
    }

    public class ProxyHostManager : IProxyHostManager
    {
        private readonly DbContext _dbContext;
        private readonly IProxyConfigurationService _proxyConfigService;

        public ProxyHostManager(DbContext dbContext, IProxyConfigurationService proxyConfigService)
        {
            _dbContext = dbContext;
            _proxyConfigService = proxyConfigService;
        }

        public async Task<List<ProxyHost>> GetAllAsync()
        {
            return await _dbContext.Set<ProxyHost>()
                .OrderBy(p => p.DomainName)
                .ToListAsync();
        }

        public async Task<ProxyHost?> GetByIdAsync(int id)
        {
            return await _dbContext.Set<ProxyHost>().FindAsync(id);
        }

        public async Task<ProxyHost?> GetByDomainAsync(string domain)
        {
            return await _dbContext.Set<ProxyHost>()
                .FirstOrDefaultAsync(p => p.DomainName == domain);
        }

        public async Task<ProxyHost> CreateAsync(ProxyHost proxyHost)
        {
            var existing = await GetByDomainAsync(proxyHost.DomainName);
            if (existing != null)
            {
                throw new InvalidOperationException($"A proxy host for domain '{proxyHost.DomainName}' already exists.");
            }

            proxyHost.CreatedAt = DateTime.UtcNow;
            proxyHost.UpdatedAt = DateTime.UtcNow;

            _dbContext.Set<ProxyHost>().Add(proxyHost);
            await _dbContext.SaveChangesAsync();

            await _proxyConfigService.ReloadConfigurationAsync();

            return proxyHost;
        }

        public async Task<ProxyHost> UpdateAsync(ProxyHost proxyHost)
        {
            var existing = await GetByIdAsync(proxyHost.Id);
            if (existing == null)
            {
                throw new InvalidOperationException($"Proxy host with ID {proxyHost.Id} not found.");
            }

            var duplicate = await _dbContext.Set<ProxyHost>()
                .FirstOrDefaultAsync(p => p.DomainName == proxyHost.DomainName && p.Id != proxyHost.Id);
            
            if (duplicate != null)
            {
                throw new InvalidOperationException($"A proxy host for domain '{proxyHost.DomainName}' already exists.");
            }

            proxyHost.UpdatedAt = DateTime.UtcNow;
            _dbContext.Entry(existing).CurrentValues.SetValues(proxyHost);
            await _dbContext.SaveChangesAsync();

            await _proxyConfigService.ReloadConfigurationAsync();

            return proxyHost;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var proxyHost = await GetByIdAsync(id);
            if (proxyHost == null)
            {
                return false;
            }

            _dbContext.Set<ProxyHost>().Remove(proxyHost);
            await _dbContext.SaveChangesAsync();

            await _proxyConfigService.ReloadConfigurationAsync();

            return true;
        }

        public async Task<bool> ToggleEnabledAsync(int id)
        {
            var proxyHost = await GetByIdAsync(id);
            if (proxyHost == null)
            {
                return false;
            }

            proxyHost.IsEnabled = !proxyHost.IsEnabled;
            proxyHost.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            await _proxyConfigService.ReloadConfigurationAsync();

            return true;
        }
    }
}
