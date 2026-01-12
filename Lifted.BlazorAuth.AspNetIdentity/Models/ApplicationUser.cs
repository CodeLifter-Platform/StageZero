using Microsoft.AspNetCore.Identity;

namespace Lifted.BlazorAuth.AspNetIdentity.Models;

/// <summary>
/// Application user entity extending IdentityUser with additional properties.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// User's first name.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// User's last name.
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Date and time when the user was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Date and time of the user's last login.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Indicates whether the user is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Indicates whether the user must change their password on next login.
    /// </summary>
    public bool RequiresPasswordChange { get; set; } = false;

    /// <summary>
    /// User's full name (computed property).
    /// </summary>
    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>
/// Authentication result returned from login attempts.
/// </summary>
public class AuthResult
{
    public bool Success { get; set; }
    public ApplicationUser? User { get; set; }
    public string? ErrorMessage { get; set; }
    public bool RequiresTwoFactor { get; set; }
    public bool IsLockedOut { get; set; }
    public bool IsNotAllowed { get; set; }

    public static AuthResult Succeeded(ApplicationUser user) => new() { Success = true, User = user };
    public static AuthResult Failed(string error) => new() { Success = false, ErrorMessage = error };
    public static AuthResult TwoFactorRequired() => new() { Success = false, RequiresTwoFactor = true };
    public static AuthResult LockedOut() => new() { Success = false, IsLockedOut = true, ErrorMessage = "Account is locked out" };
    public static AuthResult NotAllowed() => new() { Success = false, IsNotAllowed = true, ErrorMessage = "Sign in not allowed" };
}

