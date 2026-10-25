using System.ComponentModel.DataAnnotations;

namespace StageZero.Models;

/// <summary>
/// A Cloudflare zone StageZero manages records in. The API token is not per zone: every
/// provider uses the one saved on the Settings page (<see cref="CloudflareCredential"/>).
/// </summary>
public class DnsProvider
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string ProviderType { get; set; } = string.Empty; // "Cloudflare", etc.

    [MaxLength(100)]
    public string? ZoneId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }

    // Navigation property
    public ICollection<DnsRecord> DnsRecords { get; set; } = new List<DnsRecord>();
}

