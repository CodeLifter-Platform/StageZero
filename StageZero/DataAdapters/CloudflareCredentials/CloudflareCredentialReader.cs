using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.CloudflareCredentials;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface ICloudflareCredentialReader
{
    /// <summary>The single saved credential row, token still encrypted, or null if none is saved.</summary>
    Task<CloudflareCredential?> GetAsync();
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class CloudflareCredentialReader : ICloudflareCredentialReader
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public CloudflareCredentialReader(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<CloudflareCredential?> GetAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.CloudflareCredentials
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();
    }
}
