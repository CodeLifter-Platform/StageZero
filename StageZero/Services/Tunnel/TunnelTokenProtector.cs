using Microsoft.AspNetCore.DataProtection;

namespace StageZero.Services.Tunnel;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Encrypts and decrypts the Cloudflare API token stored in TunnelConfig.
/// Backed by ASP.NET Data Protection; keys are persisted to the app data
/// directory in Program.cs so tokens survive container restarts.
/// </summary>
public interface ITunnelTokenProtector
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

public class TunnelTokenProtector : ITunnelTokenProtector
{
    private const string Purpose = "StageZero.CloudflareApiToken.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<TunnelTokenProtector> _logger;

    public TunnelTokenProtector(
        IDataProtectionProvider provider,
        ILogger<TunnelTokenProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

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
                "Could not decrypt the stored Cloudflare API token. The data-protection " +
                "keyring may have been lost; re-run tunnel setup to store a new token.");
            return null;
        }
    }
}
