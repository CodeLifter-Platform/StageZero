using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.TunnelRoutes;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface ITunnelRouteWriter
{
    Task<TunnelRoute> InsertAsync(TunnelRoute route);
    Task UpdateAsync(TunnelRoute route);
    Task DeleteAsync(TunnelRoute route);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class TunnelRouteWriter : ITunnelRouteWriter
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public TunnelRouteWriter(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<TunnelRoute> InsertAsync(TunnelRoute route)
    {
        await using var db = await _factory.CreateDbContextAsync();
        route.CreatedAt = DateTime.UtcNow;
        route.UpdatedAt = DateTime.UtcNow;
        db.TunnelRoutes.Add(route);
        await db.SaveChangesAsync();
        return route;
    }

    public async Task UpdateAsync(TunnelRoute route)
    {
        await using var db = await _factory.CreateDbContextAsync();
        route.UpdatedAt = DateTime.UtcNow;
        db.TunnelRoutes.Update(route);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(TunnelRoute route)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.TunnelRoutes.Remove(route);
        await db.SaveChangesAsync();
    }
}
