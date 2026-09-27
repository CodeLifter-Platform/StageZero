namespace Lifted.BlazorAuth.Basic.Services;

/// <summary>
/// One optional box on the first-run setup form, such as a newsletter.
/// </summary>
/// <param name="Key">Stable identifier the host app receives back when the box is ticked.</param>
/// <param name="Label">The checkbox text.</param>
/// <param name="Description">One line under the label saying what the person is agreeing to.</param>
public sealed record SignupOptInChoice(string Key, string Label, string Description);

/// <summary>
/// What happened to an opt-in submission. Setup shows <see cref="Message"/> as a
/// notification either way; the account itself is never affected by this result.
/// </summary>
/// <param name="Sent">True when the request reached the host app's service and something is pending.</param>
/// <param name="Message">User-facing outcome, for example "Check your inbox to confirm."</param>
public sealed record SignupOptInResult(bool Sent, string Message);

/// <summary>
/// Optional extension point for the setup page: a host app registers an implementation
/// to add opt-in checkboxes (newsletter, product updates) to the account-creation form.
/// Nothing in the library depends on it; when no implementation is registered the form
/// shows no boxes. Implementations must never throw from <see cref="SubmitAsync"/>:
/// creating the account is the job, and a marketing signup failing must not undo it.
/// </summary>
public interface ISignupOptIn
{
    /// <summary>The boxes to offer, in display order. Empty hides the section.</summary>
    IReadOnlyList<SignupOptInChoice> Choices { get; }

    /// <summary>
    /// Called once after the account exists, only when at least one box was ticked, with
    /// the keys of the ticked boxes. Runs best-effort: report the outcome, never throw.
    /// </summary>
    Task<SignupOptInResult> SubmitAsync(
        string email,
        IReadOnlyCollection<string> selectedKeys,
        CancellationToken cancellationToken = default);
}
