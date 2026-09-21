using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StageZero.Models;

/// <summary>
/// Represents a DNS provider account (e.g., Cloudflare).
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

    /// <summary>The Cloudflare API token as stored: encrypted (ICloudflareTokenProtector).</summary>
    [Required]
    public string ProtectedApiToken { get; set; } = string.Empty;

    /// <summary>
    /// The token in the clear, in memory only: filled by the readers, encrypted into
    /// <see cref="ProtectedApiToken"/> by the writer. Never persisted.
    /// </summary>
    [NotMapped]
    public string ApiToken { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ZoneId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }

    // Navigation property
    public ICollection<DnsRecord> DnsRecords { get; set; } = new List<DnsRecord>();
}

