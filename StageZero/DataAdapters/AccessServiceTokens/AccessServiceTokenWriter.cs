using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.AccessServiceTokens;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IAccessServiceTokenWriter
{
    /// <summary>Inserts, or updates the existing row for the same Cloudflare token ID.</summary>
    Task<AccessServiceToken> UpsertAsync(AccessServiceToken token);

    /// <summary>Removes the local record. No-op if it is not there.</summary>
    Task DeleteAsync(string cloudflareTokenId);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class AccessServiceTokenWriter : IAccessServiceTokenWriter
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public AccessServiceTokenWriter(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<AccessServiceToken> UpsertAsync(AccessServiceToken token)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var existing = await db.AccessServiceTokens
            .FirstOrDefaultAsync(t => t.CloudflareTokenId == token.CloudflareTokenId);

        if (existing is null)
        {
            token.CreatedAt = token.CreatedAt == default ? DateTime.UtcNow : token.CreatedAt;
            db.AccessServiceTokens.Add(token);
            await db.SaveChangesAsync();
            return token;
        }

        existing.Name = token.Name;
        existing.ClientId = token.ClientId ?? existing.ClientId;
        existing.Duration = token.Duration ?? existing.Duration;
        existing.ExpiresAt = token.ExpiresAt ?? existing.ExpiresAt;

        // Never downgrade the provenance flag: a token StageZero minted stays StageZero's
        // to delete even if a later run rediscovers it by name.
        existing.CreatedByStageZero = existing.CreatedByStageZero || token.CreatedByStageZero;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task DeleteAsync(string cloudflareTokenId)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var existing = await db.AccessServiceTokens
            .FirstOrDefaultAsync(t => t.CloudflareTokenId == cloudflareTokenId);

        if (existing is null)
        {
            return;
        }

        db.AccessServiceTokens.Remove(existing);
        await db.SaveChangesAsync();
    }
}
