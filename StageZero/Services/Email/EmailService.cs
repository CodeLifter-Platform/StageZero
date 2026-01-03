using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StageZero.Services.Email;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IEmailService
{
    Task SendVerificationCodeAsync(string toEmail, string code);
    Task SendPasswordResetCodeAsync(string toEmail, string code);
    Task<bool> IsConfiguredAsync();
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class EmailService : IEmailService, Lifted.BlazorAuth.Basic.Services.IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> IsConfiguredAsync()
    {
        var smtpHost = _configuration["Email:SmtpHost"];
        var smtpPort = _configuration["Email:SmtpPort"];
        var fromEmail = _configuration["Email:FromEmail"];

        return !string.IsNullOrEmpty(smtpHost) &&
               !string.IsNullOrEmpty(smtpPort) &&
               !string.IsNullOrEmpty(fromEmail);
    }

    public async Task SendVerificationCodeAsync(string toEmail, string code)
    {
        try
        {
            var isConfigured = await IsConfiguredAsync();
            if (!isConfigured)
            {
                _logger.LogWarning("Email service is not configured. Verification code: {Code}", code);
                // In development, just log the code instead of sending email
                return;
            }

            var smtpHost = _configuration["Email:SmtpHost"];
            var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
            var smtpUsername = _configuration["Email:SmtpUsername"];
            var smtpPassword = _configuration["Email:SmtpPassword"];
            var fromEmail = _configuration["Email:FromEmail"];
            var fromName = _configuration["Email:FromName"] ?? "StageZero";

            using var smtpClient = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(smtpUsername, smtpPassword)
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = "StageZero - Email Verification Code",
                Body = $@"
<html>
<body style='font-family: Arial, sans-serif;'>
    <h2>Email Verification</h2>
    <p>Your verification code is:</p>
    <h1 style='color: #594AE2; letter-spacing: 5px;'>{code}</h1>
    <p>This code will expire in 15 minutes.</p>
    <p>If you didn't request this code, please ignore this email.</p>
    <hr>
    <p style='color: #666; font-size: 12px;'>StageZero</p>
</body>
</html>",
                IsBodyHtml = true
            };

            mailMessage.To.Add(toEmail);

            await smtpClient.SendMailAsync(mailMessage);
            _logger.LogInformation("Verification code sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification email to {Email}", toEmail);
            throw new EmailServiceException("Could not send verification email", ex);
        }
    }

    public async Task SendPasswordResetCodeAsync(string toEmail, string code)
    {
        try
        {
            var isConfigured = await IsConfiguredAsync();
            if (!isConfigured)
            {
                _logger.LogWarning("Email service is not configured. Password reset code: {Code}", code);
                // In development, just log the code instead of sending email
                return;
            }

            var smtpHost = _configuration["Email:SmtpHost"];
            var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
            var smtpUsername = _configuration["Email:SmtpUsername"];
            var smtpPassword = _configuration["Email:SmtpPassword"];
            var fromEmail = _configuration["Email:FromEmail"];
            var fromName = _configuration["Email:FromName"] ?? "StageZero";

            using var smtpClient = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(smtpUsername, smtpPassword)
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = "StageZero - Password Reset Code",
                Body = $@"
<html>
<body style='font-family: Arial, sans-serif;'>
    <h2>Password Reset Request</h2>
    <p>You have requested to reset your password. Your password reset code is:</p>
    <h1 style='color: #594AE2; letter-spacing: 5px;'>{code}</h1>
    <p>This code will expire in 15 minutes.</p>
    <p>If you didn't request this password reset, please ignore this email and your password will remain unchanged.</p>
    <hr>
    <p style='color: #666; font-size: 12px;'>StageZero</p>
</body>
</html>",
                IsBodyHtml = true
            };

            mailMessage.To.Add(toEmail);

            await smtpClient.SendMailAsync(mailMessage);
            _logger.LogInformation("Password reset code sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
            throw new EmailServiceException("Could not send password reset email", ex);
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class EmailServiceException : Exception
{
    public EmailServiceException(string message) : base(message) { }
    public EmailServiceException(string message, Exception innerException) : base(message, innerException) { }
}

