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

    /// <summary>
    /// Whether StageZero can keep a record of this type pointed at the public IP. Only A
    /// records: the IP lookup finds the IPv4 address, and writing that into an AAAA record
    /// fails at Cloudflare on every check. (A CNAME points at a name, not an address.)
    /// </summary>
    public static bool SupportsAutoUpdate(string recordType) => recordType == "A";

    /// <summary>Why <see cref="SupportsAutoUpdate"/> says no, for the UI.</summary>
    public static string? AutoUpdateUnavailableReason(string recordType) => recordType switch
    {
        "A" => null,
        "AAAA" => "StageZero detects your IPv4 address only, so AAAA records aren't auto-updated.",
        "CNAME" => "A CNAME points at a name, not an IP address, so it isn't auto-updated.",
        _ => "Only A records are auto-updated."
    };
}

