using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.TunnelConfigs;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface ITunnelConfigReader
{
    /// <summary>The single tunnel configuration row, or null if setup has not run.</summary>
    Task<TunnelConfig?> GetAsync();
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class TunnelConfigReader : ITunnelConfigReader
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public TunnelConfigReader(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<TunnelConfig?> GetAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.TunnelConfigs
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();
    }
}
