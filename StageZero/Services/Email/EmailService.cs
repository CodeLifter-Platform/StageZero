using System.Net;
using System.Net.Mail;
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

/// <summary>
/// Sends the one-time codes the auth flows need over SMTP. When SMTP is not configured
/// the code is written to the log instead, and the auth pages tell the user to look there.
/// </summary>
public class EmailService : IEmailService, Lifted.BlazorAuth.Basic.Services.IEmailService
{
    // Locked StageZero accent, light variant (Application/Theme/StageZeroTheme.cs).
    // Email clients render on their own background, so the light-theme value is used.
    private const string AccentColor = "#0f766e";

    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(20);

    private readonly EmailOptions _options;
    private readonly ILogger<EmailService> _logger;

    public EmailService(EmailOptions options, ILogger<EmailService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<bool> IsConfiguredAsync() => Task.FromResult(_options.IsConfigured);

    public Task SendVerificationCodeAsync(string toEmail, string code) =>
        SendCodeAsync(
            toEmail,
            code,
            kind: "verification",
            subject: "StageZero - Email Verification Code",
            heading: "Email Verification",
            intro: "Your verification code is:",
            outro: "If you didn't request this code, please ignore this email.");

    public Task SendPasswordResetCodeAsync(string toEmail, string code) =>
        SendCodeAsync(
            toEmail,
            code,
            kind: "password reset",
            subject: "StageZero - Password Reset Code",
            heading: "Password Reset Request",
            intro: "You have requested to reset your password. Your password reset code is:",
            outro: "If you didn't request this password reset, please ignore this email and your password will remain unchanged.");

    private async Task SendCodeAsync(
        string toEmail, string code, string kind, string subject, string heading, string intro, string outro)
    {
        if (!_options.IsConfigured)
        {
            // Deliberate: the code has to reach the operator somehow, and the log is the
            // only channel left. The auth pages say this is where to look.
            _logger.LogWarning(
                "SMTP is not configured ({Missing} not set), so the {Kind} code for {Email} was written here instead of emailed. {Label} code: {Code}",
                string.Join(", ", _options.MissingSettings), kind, toEmail, char.ToUpperInvariant(kind[0]) + kind[1..], code);
            return;
        }

        try
        {
            using var client = CreateClient();
            using var message = BuildMessage(toEmail, subject, BuildBody(heading, intro, code, outro));

            // SmtpClient.Timeout only covers the synchronous API; the token bounds the
            // async send so a silent relay cannot leave the setup page spinning forever.
            using var timeout = new CancellationTokenSource(SendTimeout);
            await client.SendMailAsync(message, timeout.Token);

            _logger.LogInformation(
                "{Kind} code sent to {Email} via {SmtpHost}:{SmtpPort}",
                kind, toEmail, _options.SmtpHost, _options.SmtpPort);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send the {Kind} code to {Email} via {SmtpHost}:{SmtpPort} ({Transport})",
                kind, toEmail, _options.SmtpHost, _options.SmtpPort, _options.Describe());

            // The server's reason is what the operator needs to fix the configuration, so
            // it travels with the exception instead of staying buried in the log.
            throw new EmailServiceException(
                $"Could not send the {kind} email via {_options.SmtpHost}:{_options.SmtpPort}: {ex.Message}", ex);
        }
    }

    private SmtpClient CreateClient()
    {
        var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = _options.UseStartTls,
            Timeout = (int)SendTimeout.TotalMilliseconds,
        };

        // Only authenticate when a login is configured. Handing an empty credential to a
        // relay that advertises AUTH makes it attempt a login and fail.
        if (_options.UsesAuthentication)
        {
            client.Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword ?? string.Empty);
        }

        return client;
    }

    private MailMessage BuildMessage(string toEmail, string subject, string htmlBody)
    {
        var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail!, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        message.To.Add(toEmail);
        return message;
    }

    private static string BuildBody(string heading, string intro, string code, string outro)
    {
        var safeCode = WebUtility.HtmlEncode(code);

        return $@"
<html>
<body style='font-family: Arial, sans-serif;'>
    <h2>{heading}</h2>
    <p>{intro}</p>
    <h1 style='color: {AccentColor}; letter-spacing: 5px;'>{safeCode}</h1>
    <p>This code will expire in 15 minutes.</p>
    <p>{outro}</p>
    <hr>
    <p style='color: #666; font-size: 12px;'>StageZero</p>
</body>
</html>";
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
