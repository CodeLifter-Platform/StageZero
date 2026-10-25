using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.CloudflareCredentials;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface ICloudflareCredentialWriter
{
    /// <summary>Inserts the credential row, or replaces it in place if one exists.</summary>
    Task<CloudflareCredential> UpsertAsync(CloudflareCredential credential);

    /// <summary>Records a successful verify without touching the token.</summary>
    Task MarkVerifiedAsync(DateTime verifiedAt, DateTime? expiresOn);

    Task DeleteAsync();
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class CloudflareCredentialWriter : ICloudflareCredentialWriter
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public CloudflareCredentialWriter(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<CloudflareCredential> UpsertAsync(CloudflareCredential credential)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var existing = await db.CloudflareCredentials.OrderBy(c => c.Id).FirstOrDefaultAsync();
        credential.UpdatedAt = DateTime.UtcNow;

        if (existing is null)
        {
            db.CloudflareCredentials.Add(credential);
        }
        else
        {
            existing.ProtectedApiToken = credential.ProtectedApiToken;
            existing.AccountId = credential.AccountId;
            existing.AccountName = credential.AccountName;
            existing.TokenId = credential.TokenId;
            existing.ExpiresOn = credential.ExpiresOn;
            existing.VerifiedAt = credential.VerifiedAt;
            existing.UpdatedAt = credential.UpdatedAt;
            credential = existing;
        }

        await db.SaveChangesAsync();
        return credential;
    }

    public async Task MarkVerifiedAsync(DateTime verifiedAt, DateTime? expiresOn)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.CloudflareCredentials.OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (existing is null) return;

        existing.VerifiedAt = verifiedAt;
        existing.ExpiresOn = expiresOn;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.CloudflareCredentials.ToListAsync();
        if (all.Count == 0) return;

        db.CloudflareCredentials.RemoveRange(all);
        await db.SaveChangesAsync();
    }
}
