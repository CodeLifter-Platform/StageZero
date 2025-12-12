using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.IpChecks;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IIpCheckWriter
{
    Task<IpCheck> InsertAsync(IpCheck ipCheck);
    Task DeleteAsync(IpCheck ipCheck);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class IpCheckWriter : IIpCheckWriter
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public IpCheckWriter(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IpCheck> InsertAsync(IpCheck ipCheck)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.IpChecks.Add(ipCheck);
        await db.SaveChangesAsync();
        return ipCheck;
    }

    public async Task DeleteAsync(IpCheck ipCheck)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.IpChecks.Remove(ipCheck);
        await db.SaveChangesAsync();
    }
}

