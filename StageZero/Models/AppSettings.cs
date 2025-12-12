using System.ComponentModel.DataAnnotations;

namespace StageZero.Models;

/// <summary>
/// Application settings stored in the database.
/// </summary>
public class AppSettings
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

