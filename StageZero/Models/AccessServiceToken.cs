using System.ComponentModel.DataAnnotations;

namespace StageZero.Models;

/// <summary>
/// A Cloudflare Access service token StageZero knows about. The client secret is never
/// stored — Cloudflare only returns it at creation, and it is handed to the caller once.
///
/// The row exists so teardown can tell a token StageZero minted (safe to delete once no
/// route references it) from one an operator created elsewhere (never deleted).
/// </summary>
public class AccessServiceToken
{
    [Key]
    public int Id { get; set; }

    /// <summary>Cloudflare's UUID for the token.</summary>
    [Required]
    [MaxLength(100)]
    public string CloudflareTokenId { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Sent by clients in the CF-Access-Client-Id header. Not a secret, so it is safe to
    /// store and show — unlike its paired client secret.
    /// </summary>
    [MaxLength(255)]
    public string? ClientId { get; set; }

    /// <summary>
    /// True when StageZero minted the token. Only these are deleted during teardown;
    /// tokens adopted from the account are left alone.
    /// </summary>
    public bool CreatedByStageZero { get; set; }

    [MaxLength(50)]
    public string? Duration { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }
}
