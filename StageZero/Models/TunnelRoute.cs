using System.ComponentModel.DataAnnotations;

namespace StageZero.Models;

/// <summary>
/// Represents a single Cloudflare Tunnel ingress rule: a public hostname and the
/// local service it forwards to. Cloudflare terminates TLS at the edge, so there
/// is nothing certificate-related to track here.
/// </summary>
public class TunnelRoute
{
    [Key]
    public int Id { get; set; }

    /// <summary>Public hostname, e.g. "app.example.com".</summary>
    [Required]
    [MaxLength(255)]
    public string DomainName { get; set; } = string.Empty;

    /// <summary>Scheme used to reach the local service: "http" or "https".</summary>
    [Required]
    [MaxLength(10)]
    public string ForwardScheme { get; set; } = "http";

    /// <summary>Local host or IP the connector forwards to, e.g. "localhost".</summary>
    [Required]
    [MaxLength(255)]
    public string ForwardHost { get; set; } = string.Empty;

    [Required]
    public int ForwardPort { get; set; }

    /// <summary>
    /// Disabled routes are excluded from the ingress rules pushed to Cloudflare but
    /// keep their CNAME, so re-enabling is immediate.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The origin URL sent to Cloudflare as the ingress rule's service.</summary>
    public string ForwardUrl => $"{ForwardScheme}://{ForwardHost}:{ForwardPort}";
}
