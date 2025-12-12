using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.DnsRecords;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IDnsRecordWriter
{
    Task<DnsRecord> InsertAsync(DnsRecord record);
    Task UpdateAsync(DnsRecord record);
    Task DeleteAsync(DnsRecord record);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class DnsRecordWriter : IDnsRecordWriter
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public DnsRecordWriter(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<DnsRecord> InsertAsync(DnsRecord record)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.DnsRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    public async Task UpdateAsync(DnsRecord record)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.DnsRecords.Update(record);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(DnsRecord record)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.DnsRecords.Remove(record);
        await db.SaveChangesAsync();
    }
}

