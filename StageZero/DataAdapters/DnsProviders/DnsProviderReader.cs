using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.DnsProviders;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IDnsProviderReader
{
    Task<DnsProvider?> GetByIdAsync(int id);
    Task<List<DnsProvider>> GetAllAsync();
    Task<List<DnsProvider>> GetActiveAsync();
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class DnsProviderReader : IDnsProviderReader
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public DnsProviderReader(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<DnsProvider?> GetByIdAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.DnsProviders
            .Include(p => p.DnsRecords)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<DnsProvider>> GetAllAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.DnsProviders
            .Include(p => p.DnsRecords)
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<List<DnsProvider>> GetActiveAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.DnsProviders
            .Include(p => p.DnsRecords)
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }
}

