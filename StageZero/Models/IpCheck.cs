using System.ComponentModel.DataAnnotations;

namespace StageZero.Models;

/// <summary>
/// A run of checks that all found the same public IP: one row from the check that first saw
/// it until the next change. A stable IP updates this row instead of adding one every
/// interval, so the table grows with changes, not with time.
/// </summary>
public class IpCheck
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(45)] // IPv6 max length
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>When a check first found this address.</summary>
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When a check last confirmed this address.</summary>
    public DateTime LastConfirmedAt { get; set; } = DateTime.UtcNow;

    /// <summary>How many checks found this address in this run.</summary>
    public int Confirmations { get; set; } = 1;

    /// <summary>
    /// True if this IP is different from the previous check
    /// </summary>
    public bool IsChanged { get; set; }

    /// <summary>
    /// The previous IP address if this was a change
    /// </summary>
    [MaxLength(45)]
    public string? PreviousIpAddress { get; set; }
}

