using Lifted.BlazorAuth.Basic.Services;
using Microsoft.Extensions.Logging;

namespace StageZero.Services.Auth;

/// <summary>
/// StageZero does not send email. The one-time codes the auth flows produce (password
/// reset, re-verifying a changed address) are written to the server log instead, in a
/// banner that cannot be missed. The reasoning: this is a self-hosted tool, and whoever
/// can read its log controls the server, which is exactly who is allowed to reset the
/// admin password. Someone who cannot read the log is not the operator.
/// </summary>
public sealed class ServerLogCodeService : IEmailService
{
    public const string PasswordResetHeading = "STAGEZERO PASSWORD RESET CODE";
    public const string VerificationHeading = "STAGEZERO EMAIL VERIFICATION CODE";

    private const string Rule = "==============================================================================";

    private readonly ILogger<ServerLogCodeService> _logger;

    public ServerLogCodeService(ILogger<ServerLogCodeService> logger)
    {
        _logger = logger;
    }

    /// <summary>Always false: the auth pages then tell the user the code is in the server log.</summary>
    public Task<bool> IsConfiguredAsync() => Task.FromResult(false);

    /// <summary>What the pages tell the user to search the log for: the banner headings.</summary>
    public string LoggedPasswordResetCodeHeading => PasswordResetHeading;

    public string LoggedVerificationCodeHeading => VerificationHeading;

    public Task SendPasswordResetCodeAsync(string toEmail, string code)
    {
        WriteBanner(PasswordResetHeading, toEmail, code, "Enter it on /reset-password within 15 minutes.");
        return Task.CompletedTask;
    }

    public Task SendVerificationCodeAsync(string toEmail, string code)
    {
        WriteBanner(VerificationHeading, toEmail, code, "Enter it in the verification dialog within 15 minutes.");
        return Task.CompletedTask;
    }

    /// <summary>The banner as one log entry, so it stays together in any sink.</summary>
    public static string FormatBanner(string heading, string account, string code, string instruction) =>
        string.Join(Environment.NewLine,
            "",
            Rule,
            $"  {heading}",
            "",
            $"  Account:  {account}",
            $"  Code:     {code}",
            "",
            $"  {instruction}",
            "  StageZero does not send email. This log entry is the only place the code appears.",
            Rule);

    private void WriteBanner(string heading, string account, string code, string instruction)
    {
        // Warning level on purpose: it colours the entry in console sinks and survives
        // an Information filter, and a reset code is something the operator asked for.
        _logger.LogWarning("{Banner}", FormatBanner(heading, account, code, instruction));
    }
}
