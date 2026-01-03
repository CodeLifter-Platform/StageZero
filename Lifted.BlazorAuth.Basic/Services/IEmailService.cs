namespace Lifted.BlazorAuth.Basic.Services;

/// <summary>
/// Interface for email service used by authentication.
/// Implement this interface in your application to provide email functionality.
/// </summary>
public interface IEmailService
{
    Task SendVerificationCodeAsync(string toEmail, string code);
    Task SendPasswordResetCodeAsync(string toEmail, string code);
    Task<bool> IsConfiguredAsync();
}

