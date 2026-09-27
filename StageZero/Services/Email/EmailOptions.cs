using Microsoft.Extensions.Configuration;

namespace StageZero.Services.Email;

/// <summary>
/// SMTP settings for verification and password-reset emails, read from the
/// <c>Email</c> configuration section. In Docker and <c>.env</c> these arrive as
/// <c>Email__SmtpHost</c>, <c>Email__FromEmail</c>, and so on.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public const int DefaultPort = 587;

    /// <summary>SMTP server host name or IP. Required.</summary>
    public string? SmtpHost { get; init; }

    /// <summary>SMTP port. Defaults to 587 (submission with STARTTLS).</summary>
    public int SmtpPort { get; init; } = DefaultPort;

    /// <summary>Login for the SMTP account. Leave empty for a relay that needs no authentication.</summary>
    public string? SmtpUsername { get; init; }

    /// <summary>Password or app password for the SMTP account. Only used when a username is set.</summary>
    public string? SmtpPassword { get; init; }

    /// <summary>Sender address. Required.</summary>
    public string? FromEmail { get; init; }

    /// <summary>Sender display name.</summary>
    public string FromName { get; init; } = "StageZero";

    /// <summary>
    /// Upgrade the connection with STARTTLS after connecting. Required by Gmail, Outlook and
    /// every public relay; switch off only for a plaintext relay on a trusted network (a
    /// local Postfix, MailHog, or a mail sidecar in the same compose stack).
    /// </summary>
    public bool UseStartTls { get; init; } = true;

    /// <summary>True when enough is set to attempt delivery.</summary>
    public bool IsConfigured => MissingSettings.Count == 0;

    /// <summary>True when the client should authenticate before sending.</summary>
    public bool UsesAuthentication => !string.IsNullOrWhiteSpace(SmtpUsername);

    /// <summary>The required settings that are still empty, by their environment-variable names.</summary>
    public IReadOnlyList<string> MissingSettings
    {
        get
        {
            var missing = new List<string>(2);
            if (string.IsNullOrWhiteSpace(SmtpHost)) missing.Add("Email__SmtpHost");
            if (string.IsNullOrWhiteSpace(FromEmail)) missing.Add("Email__FromEmail");
            return missing;
        }
    }

    /// <summary>
    /// Builds the options from configuration. Blank or unparsable values fall back to the
    /// defaults rather than failing startup, so an empty <c>Email__SmtpPort=</c> line in
    /// <c>.env</c> still means 587.
    /// </summary>
    public static EmailOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        return new EmailOptions
        {
            SmtpHost = Clean(section["SmtpHost"]),
            SmtpPort = int.TryParse(section["SmtpPort"], out var port) && port > 0 ? port : DefaultPort,
            SmtpUsername = Clean(section["SmtpUsername"]),
            SmtpPassword = section["SmtpPassword"] is { Length: > 0 } password ? password : null,
            FromEmail = Clean(section["FromEmail"]),
            FromName = Clean(section["FromName"]) ?? "StageZero",
            UseStartTls = !bool.TryParse(section["UseStartTls"], out var startTls) || startTls,
        };
    }

    /// <summary>
    /// One line for the startup log. Never includes the password.
    /// </summary>
    public string Describe()
    {
        if (!IsConfigured)
        {
            return $"not configured ({string.Join(", ", MissingSettings)} not set)";
        }

        var auth = UsesAuthentication ? $"as {SmtpUsername}" : "no authentication";
        var tls = UseStartTls ? "STARTTLS" : "plaintext";
        return $"{SmtpHost}:{SmtpPort} ({tls}, {auth}), from {FromName} <{FromEmail}>";
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
