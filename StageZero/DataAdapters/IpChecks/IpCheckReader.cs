using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.IpChecks;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IIpCheckReader
{
    Task<IpCheck?> GetLatestAsync();
    Task<List<IpCheck>> GetRecentAsync(int count = 100);
    Task<IpCheck?> GetByIdAsync(int id);
    Task<List<IpCheckGroup>> GetGroupedByIpAsync();
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class IpCheckReader : IIpCheckReader
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public IpCheckReader(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IpCheck?> GetLatestAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.IpChecks
            .AsNoTracking()
            .OrderByDescending(c => c.CheckedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<IpCheck>> GetRecentAsync(int count = 100)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.IpChecks
            .AsNoTracking()
            .OrderByDescending(c => c.CheckedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<IpCheck?> GetByIdAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.IpChecks.FindAsync(id);
    }

    public async Task<List<IpCheckGroup>> GetGroupedByIpAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.IpChecks
            .AsNoTracking()
            .GroupBy(c => c.IpAddress)
            .Select(g => new IpCheckGroup
            {
                IpAddress = g.Key,
                LastCheckedAt = g.Max(c => c.CheckedAt),
                CheckCount = g.Count(),
                FirstSeenAt = g.Min(c => c.CheckedAt)
            })
            .OrderByDescending(g => g.LastCheckedAt)
            .ToListAsync();
    }
}

