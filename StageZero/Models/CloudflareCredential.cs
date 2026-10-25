using System.ComponentModel.DataAnnotations;

namespace StageZero.Models;

/// <summary>
/// Single-row table holding the one Cloudflare API token StageZero uses for everything — DNS
/// updates, the tunnel, and Access — and the account it belongs to. Managed on the Settings page.
/// </summary>
public class CloudflareCredential
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The API token encrypted with ASP.NET Data Protection. Never store the raw token —
    /// go through ICloudflareTokenProtector.
    /// </summary>
    [Required]
    public string ProtectedApiToken { get; set; } = string.Empty;

    /// <summary>
    /// The account the tunnel and Access live in. Null for a token with no account-scoped
    /// permissions, which can still manage DNS.
    /// </summary>
    [MaxLength(100)]
    public string? AccountId { get; set; }

    /// <summary>Account name, kept for display so the UI need not re-query Cloudflare.</summary>
    [MaxLength(255)]
    public string? AccountName { get; set; }

    /// <summary>Cloudflare's id for the token (not a secret), to find it in the dashboard.</summary>
    [MaxLength(100)]
    public string? TokenId { get; set; }

    /// <summary>When Cloudflare says the token stops working; null for no expiry.</summary>
    public DateTime? ExpiresOn { get; set; }

    /// <summary>When the token last passed Cloudflare's verify call.</summary>
    public DateTime? VerifiedAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
