using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.TunnelConfigs;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface ITunnelConfigWriter
{
    /// <summary>Inserts the configuration row, or updates it in place if one exists.</summary>
    Task<TunnelConfig> UpsertAsync(TunnelConfig config);
    Task DeleteAsync();
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class TunnelConfigWriter : ITunnelConfigWriter
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public TunnelConfigWriter(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<TunnelConfig> UpsertAsync(TunnelConfig config)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var existing = await db.TunnelConfigs.OrderBy(c => c.Id).FirstOrDefaultAsync();
        config.UpdatedAt = DateTime.UtcNow;

        if (existing is null)
        {
            db.TunnelConfigs.Add(config);
        }
        else
        {
            existing.CloudflareAccountId = config.CloudflareAccountId;
            existing.CloudflareZoneId = config.CloudflareZoneId;
            existing.CloudflareZoneName = config.CloudflareZoneName;
            existing.ProtectedApiToken = config.ProtectedApiToken;
            existing.TunnelId = config.TunnelId;
            existing.TunnelName = config.TunnelName;
            existing.UpdatedAt = config.UpdatedAt;
            config = existing;
        }

        await db.SaveChangesAsync();
        return config;
    }

    public async Task DeleteAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.TunnelConfigs.ToListAsync();
        if (all.Count == 0) return;

        db.TunnelConfigs.RemoveRange(all);
        await db.SaveChangesAsync();
    }
}
