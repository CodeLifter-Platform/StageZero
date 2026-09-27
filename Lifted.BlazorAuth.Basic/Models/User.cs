using System.ComponentModel.DataAnnotations;

namespace Lifted.BlazorAuth.Basic.Models;

/// <summary>
/// User entity for email/password authentication.
/// </summary>
public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public bool EmailVerified { get; set; } = false;

    [MaxLength(10)]
    public string? EmailVerificationCode { get; set; }

    public DateTime? EmailVerificationCodeExpiry { get; set; }

    [MaxLength(10)]
    public string? PasswordResetCode { get; set; }

    public DateTime? PasswordResetCodeExpiry { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    public bool IsActive { get; set; } = true;

    public bool RequiresPasswordChange { get; set; } = false;

    /// <summary>Wrong passwords in a row. Reaching the limit locks the account for a while.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>Until when sign-in is refused after too many wrong passwords.</summary>
    public DateTime? LockoutEndsAt { get; set; }

    /// <summary>Wrong guesses at the current reset code; at the limit the code is void.</summary>
    public int PasswordResetAttempts { get; set; }

    /// <summary>Wrong guesses at the current verification code; at the limit the code is void.</summary>
    public int EmailVerificationAttempts { get; set; }
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

