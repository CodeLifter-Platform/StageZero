using System.ComponentModel.DataAnnotations;

namespace StageZero.Models;

/// <summary>
/// Single-row table holding the tunnel this instance manages. The account and API token it is
/// reached with are the Settings page's (<see cref="CloudflareCredential"/>).
/// </summary>
public class TunnelConfig
{
    [Key]
    public int Id { get; set; }

    /// <summary>Zone the tunnel's hostnames live in (CNAMEs are written here).</summary>
    [MaxLength(100)]
    public string? CloudflareZoneId { get; set; }

    /// <summary>Zone name, kept for display so the UI need not re-query Cloudflare.</summary>
    [MaxLength(255)]
    public string? CloudflareZoneName { get; set; }

    [MaxLength(100)]
    public string? TunnelId { get; set; }

    [MaxLength(255)]
    public string? TunnelName { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True once the tunnel has been created or adopted and routes can sync.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(TunnelId)
                                && !string.IsNullOrWhiteSpace(CloudflareZoneId);
}
