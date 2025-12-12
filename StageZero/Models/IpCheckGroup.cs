namespace StageZero.Models;

/// <summary>
/// Represents a group of IP checks for the same IP address.
/// </summary>
public class IpCheckGroup
{
    /// <summary>
    /// The IP address for this group
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// The most recent check time for this IP
    /// </summary>
    public DateTime LastCheckedAt { get; set; }

    /// <summary>
    /// Total number of checks for this IP
    /// </summary>
    public int CheckCount { get; set; }

    /// <summary>
    /// First time this IP was seen
    /// </summary>
    public DateTime FirstSeenAt { get; set; }
}

