namespace Lifted.BlazorAuth.Basic.Services;

/// <summary>
/// Delivers the one-time codes the auth flows produce. Implement this in your
/// application: over email, or, for a self-hosted app that never sends email, by
/// writing the code to the server log for the operator to read.
/// </summary>
public interface IEmailService
{
    Task SendVerificationCodeAsync(string toEmail, string code);
    Task SendPasswordResetCodeAsync(string toEmail, string code);

    /// <summary>
    /// True when codes actually reach the address. When false, the pages tell the user
    /// the code was written to the server log and quote the heading to look for.
    /// </summary>
    Task<bool> IsConfiguredAsync();

    /// <summary>
    /// The exact text a logged password-reset code is filed under, quoted on the
    /// forgot-password page so the operator knows what to search the log for.
    /// Override when your log banner has its own heading.
    /// </summary>
    string LoggedPasswordResetCodeHeading => "PASSWORD RESET CODE";

    /// <summary>Same, for a logged email-verification code.</summary>
    string LoggedVerificationCodeHeading => "VERIFICATION CODE";
}
