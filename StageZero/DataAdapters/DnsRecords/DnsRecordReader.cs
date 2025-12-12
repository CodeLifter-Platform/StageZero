using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.DnsRecords;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IDnsRecordReader
{
    Task<DnsRecord?> GetByIdAsync(int id);
    Task<List<DnsRecord>> GetAllAsync();
    Task<List<DnsRecord>> GetByProviderIdAsync(int providerId);
    Task<List<DnsRecord>> GetAutoUpdateRecordsAsync();
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class DnsRecordReader : IDnsRecordReader
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public DnsRecordReader(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<DnsRecord?> GetByIdAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.DnsRecords
            .Include(r => r.DnsProvider)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<List<DnsRecord>> GetAllAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.DnsRecords
            .Include(r => r.DnsProvider)
            .AsNoTracking()
            .OrderBy(r => r.RecordName)
            .ToListAsync();
    }

    public async Task<List<DnsRecord>> GetByProviderIdAsync(int providerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.DnsRecords
            .Include(r => r.DnsProvider)
            .AsNoTracking()
            .Where(r => r.DnsProviderId == providerId)
            .OrderBy(r => r.RecordName)
            .ToListAsync();
    }

    public async Task<List<DnsRecord>> GetAutoUpdateRecordsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.DnsRecords
            .Include(r => r.DnsProvider)
            .AsNoTracking()
            .Where(r => r.AutoUpdate && r.DnsProvider.IsActive)
            .ToListAsync();
    }
}

