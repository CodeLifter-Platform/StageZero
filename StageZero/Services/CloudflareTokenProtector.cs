using Microsoft.AspNetCore.DataProtection;
using StageZero.Models;

namespace StageZero.Services;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Encrypts and decrypts the Cloudflare API tokens StageZero stores — the DNS providers'
/// and the tunnel's. Backed by ASP.NET Data Protection (the dp-keys ring), so a copy of the
/// database alone doesn't give the tokens away.
/// </summary>
public interface ICloudflareTokenProtector
{
    string Protect(string plaintextToken);

    /// <summary>
    /// Returns null when the payload cannot be decrypted — usually because the
    /// data-protection keyring was lost. Callers should treat that as "re-run setup".
    /// </summary>
    string? Unprotect(string protectedToken);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class CloudflareTokenProtector : ICloudflareTokenProtector
{
    // Unchanged from when this protected only the tunnel's token, so those still decrypt.
    private const string Purpose = "StageZero.CloudflareApiToken.v1";

    // Every Data Protection payload starts with its magic header, base64url-encoded.
    private const string ProtectedPrefix = "CfDJ8";

    private readonly IDataProtector _protector;
    private readonly ILogger<CloudflareTokenProtector> _logger;

    public CloudflareTokenProtector(
        IDataProtectionProvider provider,
        ILogger<CloudflareTokenProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    /// <summary>
    /// True for a Data Protection payload. Tells a legacy plaintext token (to be encrypted)
    /// from one encrypted under a lost key ring (which must not be encrypted again).
    /// </summary>
    public static bool IsProtected(string value) => value.StartsWith(ProtectedPrefix, StringComparison.Ordinal);

    public string Protect(string plaintextToken)
    {
        return _protector.Protect(plaintextToken);
    }

    public string? Unprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Could not decrypt a stored Cloudflare API token. The data-protection " +
                "keyring may have been lost; enter the token again (tunnel setup, or the DNS provider).");
            return null;
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// READ-SIDE HELPERS
// ═══════════════════════════════════════════════════════════════

/// <summary>Decrypts stored DNS provider tokens into <see cref="DnsProvider.ApiToken"/> as the readers load them.</summary>
public static class CloudflareTokenReveal
{
    public static DnsProvider Reveal(this ICloudflareTokenProtector tokens, DnsProvider provider)
    {
        provider.ApiToken = CloudflareTokenProtector.IsProtected(provider.ProtectedApiToken)
            ? tokens.Unprotect(provider.ProtectedApiToken) ?? string.Empty
            : provider.ProtectedApiToken; // not yet encrypted (see CloudflareTokenStore)
        return provider;
    }

    public static async Task<List<DnsProvider>> RevealAll(this Task<List<DnsProvider>> query, ICloudflareTokenProtector tokens)
    {
        var providers = await query;
        providers.ForEach(p => tokens.Reveal(p));
        return providers;
    }

    public static async Task<List<DnsRecord>> RevealProviders(this Task<List<DnsRecord>> query, ICloudflareTokenProtector tokens)
    {
        var records = await query;
        foreach (var provider in records.Select(r => r.DnsProvider).Distinct())
        {
            tokens.Reveal(provider);
        }

        return records;
    }
}
