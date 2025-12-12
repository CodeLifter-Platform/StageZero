using System.ComponentModel.DataAnnotations;

namespace StageZero.Models;

/// <summary>
/// Represents a historical IP address check.
/// </summary>
public class IpCheck
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(45)] // IPv6 max length
    public string IpAddress { get; set; } = string.Empty;

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

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

