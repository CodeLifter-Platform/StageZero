using System.ComponentModel.DataAnnotations;

namespace StageZero.Models;

/// <summary>
/// Represents a DNS record to be automatically updated.
/// </summary>
public class DnsRecord
{
    [Key]
    public int Id { get; set; }

    public int DnsProviderId { get; set; }

    [Required]
    [MaxLength(255)]
    public string RecordName { get; set; } = string.Empty; // e.g., "example.com" or "subdomain.example.com"

    [Required]
    [MaxLength(10)]
    public string RecordType { get; set; } = "A"; // A, AAAA, etc.

    [MaxLength(100)]
    public string? RecordId { get; set; } // Provider-specific record ID

    public bool AutoUpdate { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUpdatedAt { get; set; }

    [MaxLength(45)]
    public string? LastIpAddress { get; set; }

    [MaxLength(255)]
    public string? Content { get; set; } // For CNAME records, stores the target domain

    // Navigation property
    public DnsProvider DnsProvider { get; set; } = null!;
}

