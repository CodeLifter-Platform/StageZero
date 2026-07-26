using System.ComponentModel.DataAnnotations;

namespace StageZero.Models;

/// <summary>
/// Single-row table holding the Cloudflare account and tunnel this instance manages.
/// </summary>
public class TunnelConfig
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string CloudflareAccountId { get; set; } = string.Empty;

    /// <summary>Zone the tunnel's hostnames live in (CNAMEs are written here).</summary>
    [MaxLength(100)]
    public string? CloudflareZoneId { get; set; }

    /// <summary>Zone name, kept for display so the UI need not re-query Cloudflare.</summary>
    [MaxLength(255)]
    public string? CloudflareZoneName { get; set; }

    /// <summary>
    /// API token encrypted with ASP.NET Data Protection. Never store the raw token —
    /// go through ITunnelTokenProtector.
    /// </summary>
    [Required]
    public string ProtectedApiToken { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? TunnelId { get; set; }

    [MaxLength(255)]
    public string? TunnelName { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True once the tunnel has been created or adopted and routes can sync.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(TunnelId)
                                && !string.IsNullOrWhiteSpace(CloudflareZoneId);
}
