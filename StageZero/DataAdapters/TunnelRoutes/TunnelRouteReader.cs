using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.TunnelRoutes;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface ITunnelRouteReader
{
    Task<TunnelRoute?> GetByIdAsync(int id);
    Task<List<TunnelRoute>> GetAllAsync();
    Task<List<TunnelRoute>> GetEnabledAsync();
    Task<TunnelRoute?> GetByDomainAsync(string domainName);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class TunnelRouteReader : ITunnelRouteReader
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public TunnelRouteReader(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<TunnelRoute?> GetByIdAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.TunnelRoutes.FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<List<TunnelRoute>> GetAllAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.TunnelRoutes
            .AsNoTracking()
            .OrderBy(r => r.DomainName)
            .ToListAsync();
    }

    /// <summary>
    /// Enabled routes ordered most-specific hostname first, which is the order
    /// Cloudflare evaluates ingress rules in.
    /// </summary>
    public async Task<List<TunnelRoute>> GetEnabledAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var routes = await db.TunnelRoutes
            .AsNoTracking()
            .Where(r => r.IsEnabled)
            .ToListAsync();

        return routes
            .OrderByDescending(r => r.DomainName.Count(c => c == '.'))
            .ThenBy(r => r.DomainName)
            .ToList();
    }

    public async Task<TunnelRoute?> GetByDomainAsync(string domainName)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.TunnelRoutes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.DomainName == domainName);
    }
}
