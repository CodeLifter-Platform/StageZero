using System.ComponentModel.DataAnnotations;

namespace StageZero.Models;

/// <summary>
/// User entity for basic username/password authentication.
/// </summary>
public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Email { get; set; }

    public bool EmailVerified { get; set; } = false;

    [MaxLength(10)]
    public string? EmailVerificationCode { get; set; }

    public DateTime? EmailVerificationCodeExpiry { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    public bool IsActive { get; set; } = true;

    public bool RequiresPasswordChange { get; set; } = false;
}

/// <summary>
/// Authentication result returned from login attempts.
/// </summary>
public class AuthResult
{
    public bool Success { get; set; }
    public User? User { get; set; }
    public string? ErrorMessage { get; set; }

    public static AuthResult Succeeded(User user) => new() { Success = true, User = user };
    public static AuthResult Failed(string error) => new() { Success = false, ErrorMessage = error };
}

