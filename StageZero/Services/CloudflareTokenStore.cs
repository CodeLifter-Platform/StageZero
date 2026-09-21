using Microsoft.EntityFrameworkCore;
using StageZero.Data;

namespace StageZero.Services;

/// <summary>
/// Encrypts DNS provider tokens that are still stored in the clear — every token saved before
/// they were protected. Runs at startup after the migrations; a SQL migration can't call Data
/// Protection. Idempotent: an encrypted token is recognised and left alone, including one
/// encrypted under a lost key ring, which must not be encrypted a second time.
/// </summary>
public static class CloudflareTokenStore
{
    public static async Task<int> ProtectLegacyTokensAsync(IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var tokens = scope.ServiceProvider.GetRequiredService<ICloudflareTokenProtector>();

        await using var db = await factory.CreateDbContextAsync();
        var plaintext = (await db.DnsProviders.ToListAsync())
            .Where(p => !CloudflareTokenProtector.IsProtected(p.ProtectedApiToken))
            .ToList();

        foreach (var provider in plaintext)
        {
            provider.ProtectedApiToken = tokens.Protect(provider.ProtectedApiToken);
        }

        if (plaintext.Count > 0)
        {
            await db.SaveChangesAsync();
            logger.LogWarning("Encrypted {Count} DNS provider token(s) that were stored in the clear", plaintext.Count);
        }

        return plaintext.Count;
    }
}
