using Microsoft.EntityFrameworkCore;
using StageZero.Data;

namespace StageZero.Services;

/// <summary>
/// Encrypts a saved Cloudflare token that is still in the clear. The migration that gave
/// StageZero one token carries over a DNS provider's when there was no tunnel, and a provider
/// saved before tokens were encrypted held it in the clear. Runs at startup after the
/// migrations; a SQL migration can't call Data Protection. Idempotent: an encrypted token is
/// recognised and left alone, including one encrypted under a lost key ring, which must not be
/// encrypted a second time.
/// </summary>
public static class CloudflareTokenStore
{
    public static async Task<int> ProtectLegacyTokensAsync(IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var tokens = scope.ServiceProvider.GetRequiredService<ICloudflareTokenProtector>();

        await using var db = await factory.CreateDbContextAsync();
        var plaintext = (await db.CloudflareCredentials.ToListAsync())
            .Where(c => !CloudflareTokenProtector.IsProtected(c.ProtectedApiToken))
            .ToList();

        foreach (var credential in plaintext)
        {
            credential.ProtectedApiToken = tokens.Protect(credential.ProtectedApiToken);
        }

        if (plaintext.Count > 0)
        {
            await db.SaveChangesAsync();
            logger.LogWarning("Encrypted {Count} Cloudflare API token(s) that were stored in the clear", plaintext.Count);
        }

        return plaintext.Count;
    }
}
