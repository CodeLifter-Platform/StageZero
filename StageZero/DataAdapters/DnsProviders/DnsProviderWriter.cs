using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.DnsProviders;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IDnsProviderWriter
{
    Task<DnsProvider> InsertAsync(DnsProvider provider);
    Task UpdateAsync(DnsProvider provider);
    Task DeleteAsync(DnsProvider provider);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class DnsProviderWriter : IDnsProviderWriter
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public DnsProviderWriter(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<DnsProvider> InsertAsync(DnsProvider provider)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.DnsProviders.Add(provider);
        await db.SaveChangesAsync();
        return provider;
    }

    public async Task UpdateAsync(DnsProvider provider)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.DnsProviders.Update(provider);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(DnsProvider provider)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.DnsProviders.Remove(provider);
        await db.SaveChangesAsync();
    }
}

