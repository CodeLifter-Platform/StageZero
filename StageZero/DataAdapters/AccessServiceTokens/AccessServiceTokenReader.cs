using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.AccessServiceTokens;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IAccessServiceTokenReader
{
    Task<List<AccessServiceToken>> GetAllAsync();
    Task<AccessServiceToken?> GetByCloudflareIdAsync(string cloudflareTokenId);
    Task<AccessServiceToken?> GetByNameAsync(string name);

    /// <summary>
    /// How many tunnel routes point at this token. Teardown uses it to decide whether a
    /// token StageZero minted is still in use elsewhere.
    /// </summary>
    Task<int> CountRoutesUsingAsync(string cloudflareTokenId, int? excludingRouteId = null);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class AccessServiceTokenReader : IAccessServiceTokenReader
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public AccessServiceTokenReader(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<AccessServiceToken>> GetAllAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.AccessServiceTokens
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<AccessServiceToken?> GetByCloudflareIdAsync(string cloudflareTokenId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.AccessServiceTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CloudflareTokenId == cloudflareTokenId);
    }

    public async Task<AccessServiceToken?> GetByNameAsync(string name)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.AccessServiceTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Name == name);
    }

    public async Task<int> CountRoutesUsingAsync(string cloudflareTokenId, int? excludingRouteId = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.TunnelRoutes
            .AsNoTracking()
            .CountAsync(r => r.Access.ServiceTokenId == cloudflareTokenId
                             && (excludingRouteId == null || r.Id != excludingRouteId));
    }
}
