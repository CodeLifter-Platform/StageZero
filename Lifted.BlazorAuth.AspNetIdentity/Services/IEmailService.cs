namespace Lifted.BlazorAuth.AspNetIdentity.Services;

/// <summary>
/// Email service interface for sending authentication-related emails.
/// Implement this interface in your application to enable email functionality.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email verification token to the specified email address.
    /// </summary>
    /// <param name="email">The email address to send to.</param>
    /// <param name="token">The verification token.</param>
    Task SendEmailVerificationAsync(string email, string token);

    /// <summary>
    /// Sends a password reset token to the specified email address.
    /// </summary>
    /// <param name="email">The email address to send to.</param>
    /// <param name="token">The password reset token.</param>
    Task SendPasswordResetAsync(string email, string token);

    /// <summary>
    /// Sends a two-factor authentication code to the specified email address.
    /// </summary>
    /// <param name="email">The email address to send to.</param>
    /// <param name="code">The two-factor authentication code.</param>
    Task SendTwoFactorCodeAsync(string email, string code);
}

