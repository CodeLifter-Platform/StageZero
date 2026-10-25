using Microsoft.AspNetCore.DataProtection;

namespace StageZero.Services;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Encrypts and decrypts the Cloudflare API token StageZero stores (the Settings page's,
/// <see cref="Models.CloudflareCredential"/>). Backed by ASP.NET Data Protection (the dp-keys ring), so a copy of the
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
    // Unchanged since this protected only the tunnel's token: tokens carried over from the
    // tunnel and the DNS providers still decrypt.
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
                "keyring may have been lost; enter the token again on the Settings page.");
            return null;
        }
    }
}
