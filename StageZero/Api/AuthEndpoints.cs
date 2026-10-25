using Lifted.BlazorAuth.Basic.DataAdapters;
using Lifted.BlazorAuth.Basic.Models;
using Lifted.BlazorAuth.Basic.Services;
using StageZero.Services.CodeLifter;

namespace StageZero.Api;

/// <summary>
/// Sign-in, first-run setup, password reset and the account setup a reset leaves owing.
/// Every rule — throttles, lockout, constant-time codes — is the auth library's
/// (<see cref="AuthService"/>); these endpoints only carry it over HTTP and hold the cookie.
/// </summary>
public static class AuthEndpoints
{
    public const int MinPasswordLength = 8;

    public static void MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithUserFacingErrors();

        auth.MapGet("/session", GetSessionAsync);
        auth.MapPost("/setup", SetupAsync);
        auth.MapPost("/login", LoginAsync);
        auth.MapPost("/logout", LogoutAsync);
        auth.MapPost("/forgot-password", ForgotPasswordAsync);
        auth.MapPost("/reset-password", ResetPasswordAsync);

        // Account setup: signed in, but owing a verified email and/or a new password.
        var account = auth.MapGroup("/account").RequireAuthorization(Session.SignedInPolicy);
        account.MapPost("/send-verification", SendVerificationAsync);
        account.MapPost("/verify-email", VerifyEmailAsync);
        account.MapPost("/change-password", ChangePasswordAsync);
    }

    // ── contracts ────────────────────────────────────────────────

    public sealed record SessionResponse(
        bool SetupRequired,
        SessionUser? User,
        bool CodeLifterRegistration,
        string PasswordResetCodeHeading,
        string VerificationCodeHeading);

    public sealed record SessionUser(string Email, bool RequiresPasswordChange, bool RequiresEmailVerification)
    {
        public bool AccountSetupRequired => RequiresPasswordChange || RequiresEmailVerification;

        public static SessionUser From(User user) =>
            new(user.Email, user.RequiresPasswordChange, !user.EmailVerified);
    }

    /// <param name="RegisterWithCodeLifter">The first-run page's "Register this install with codelifter.net", on unless unticked.</param>
    public sealed record SetupRequest(string Email, string Password, bool? RegisterWithCodeLifter);

    public sealed record SetupResponse(bool RegisteredWithCodeLifter);

    public sealed record LoginRequest(string Email, string Password);

    public sealed record EmailRequest(string Email);

    public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);

    public sealed record CodeRequest(string Code);

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    // ── handlers ─────────────────────────────────────────────────

    private static async Task<SessionResponse> GetSessionAsync(
        HttpContext http, IUserReader users, IAuthService auth, IEmailService codes, ICodeLifterRegistration registration)
    {
        var anyUser = (await users.GetAllAsync()).Count > 0;
        var user = http.User.Identity?.IsAuthenticated == true ? await auth.GetCurrentUserAsync() : null;
        return new SessionResponse(
            SetupRequired: !anyUser,
            User: user is null ? null : SessionUser.From(user),
            // Offered on the setup form only, so only before there is a user.
            CodeLifterRegistration: !anyUser && registration.Enabled,
            PasswordResetCodeHeading: codes.LoggedPasswordResetCodeHeading,
            VerificationCodeHeading: codes.LoggedVerificationCodeHeading);
    }

    private static async Task<IResult> SetupAsync(
        SetupRequest request, IUserReader users, IUserWriter writer, ICodeLifterRegistration registration, ILogger<SetupRequest> logger)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        if (email.Length == 0 || !email.Contains('@'))
            return Problem("Please enter a valid email address.");
        if ((request.Password ?? string.Empty).Length < MinPasswordLength)
            return Problem($"Password must be at least {MinPasswordLength} characters long.");

        // Setup is one-time: with any user present it is closed for good.
        if ((await users.GetAllAsync()).Count > 0)
            return Problem("Setup has already been completed.", StatusCodes.Status409Conflict);

        // The first admin is whoever reaches a fresh install first. Their address is the
        // login, not a proof of anything, so it is marked verified rather than gated on an
        // email round-trip.
        await writer.InsertAsync(new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsActive = true,
            RequiresPasswordChange = false,
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow
        });
        logger.LogInformation("First-run setup created the admin account {Email}", email);

        // Best-effort, after the account exists: who runs StageZero, for codelifter.net. It
        // subscribes the person to nothing; the email options are on Settings. Never fails setup.
        var registered = request.RegisterWithCodeLifter != false && await registration.RegisterAsync(email);

        return Results.Ok(new SetupResponse(registered));
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext http, IAuthService auth)
    {
        var result = await auth.LoginAsync(request.Email?.Trim() ?? string.Empty, request.Password ?? string.Empty);
        if (!result.Success || result.User is null)
        {
            return Problem(result.ErrorMessage ?? "Sign-in failed.", StatusCodes.Status401Unauthorized);
        }

        await Session.SignInAsync(http, result.User);
        return Results.Ok(SessionUser.From(result.User));
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, IAuthService auth)
    {
        await auth.LogoutAsync();
        await Session.SignOutAsync(http);
        return Results.NoContent();
    }

    private static async Task<IResult> ForgotPasswordAsync(EmailRequest request, IAuthService auth)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) return Problem("Email is required.");
        try
        {
            // Answers the same for an unknown address, so it can't be used to find accounts.
            await auth.SendPasswordResetCodeAsync(request.Email.Trim());
            return Results.NoContent();
        }
        catch (AuthThrottledException ex)
        {
            return Throttled(ex);
        }
    }

    private static async Task<IResult> ResetPasswordAsync(ResetPasswordRequest request, IAuthService auth)
    {
        if ((request.NewPassword ?? string.Empty).Length < MinPasswordLength)
            return Problem($"Password must be at least {MinPasswordLength} characters long.");
        try
        {
            var ok = await auth.ResetPasswordAsync(
                request.Email?.Trim() ?? string.Empty, request.Code?.Trim() ?? string.Empty, request.NewPassword!);
            return ok ? Results.NoContent() : Problem("Invalid or expired code. Please try again.");
        }
        catch (AuthThrottledException ex)
        {
            return Throttled(ex);
        }
    }

    private static async Task<IResult> SendVerificationAsync(EmailRequest request, HttpContext http, IAuthService auth)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        if (!email.Contains('@') || !email.Contains('.')) return Problem("Please enter a valid email address.");

        if (await auth.GetCurrentUserAsync() is { } user && !string.Equals(user.Email, email, StringComparison.Ordinal))
        {
            await auth.UpdateEmailAsync(email);
            await ReissueAsync(http, auth); // the stamp covers the email
        }

        return await auth.SendEmailVerificationCodeAsync(email)
            ? Results.NoContent()
            : Problem("Failed to send the verification code.");
    }

    private static async Task<IResult> VerifyEmailAsync(CodeRequest request, IAuthService auth)
    {
        var ok = await auth.VerifyEmailCodeAsync(request.Code?.Trim() ?? string.Empty);
        return ok && await auth.GetCurrentUserAsync() is { } user
            ? Results.Ok(SessionUser.From(user))
            : Problem("Invalid or expired verification code.");
    }

    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, HttpContext http, IAuthService auth)
    {
        if ((request.NewPassword ?? string.Empty).Length < MinPasswordLength)
            return Problem($"Password must be at least {MinPasswordLength} characters long.");

        if (!await auth.ChangePasswordAsync(request.CurrentPassword ?? string.Empty, request.NewPassword!))
            return Problem("Current password is incorrect.");

        // The old cookie's stamp no longer matches; this session carries on with a new one.
        var user = await ReissueAsync(http, auth);
        return Results.Ok(SessionUser.From(user));
    }

    private static async Task<User> ReissueAsync(HttpContext http, IAuthService auth)
    {
        var user = await auth.GetCurrentUserAsync() ?? throw new InvalidOperationException("No current user.");
        await Session.SignInAsync(http, user);
        return user;
    }

    private static IResult Problem(string title, int status = StatusCodes.Status400BadRequest) =>
        Results.Problem(title: title, statusCode: status);

    private static IResult Throttled(AuthThrottledException ex)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(ex.RetryAfter.TotalSeconds));
        return Results.Problem(
            title: ex.Message,
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?> { ["retryAfterSeconds"] = seconds });
    }
}
