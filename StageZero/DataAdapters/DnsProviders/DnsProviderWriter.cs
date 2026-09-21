using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;
using StageZero.Services;

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
    private readonly ICloudflareTokenProtector _tokens;

    public DnsProviderWriter(IDbContextFactory<ApplicationDbContext> factory, ICloudflareTokenProtector tokens)
    {
        _factory = factory;
        _tokens = tokens;
    }

    public async Task<DnsProvider> InsertAsync(DnsProvider provider)
    {
        Conceal(provider);
        await using var db = await _factory.CreateDbContextAsync();
        db.DnsProviders.Add(provider);
        await db.SaveChangesAsync();
        return provider;
    }

    public async Task UpdateAsync(DnsProvider provider)
    {
        Conceal(provider);
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

    /// <summary>Encrypts the in-memory token for storage. A provider read without it keeps its stored value.</summary>
    private void Conceal(DnsProvider provider)
    {
        if (!string.IsNullOrEmpty(provider.ApiToken))
        {
            provider.ProtectedApiToken = _tokens.Protect(provider.ApiToken);
        }
    }
}
