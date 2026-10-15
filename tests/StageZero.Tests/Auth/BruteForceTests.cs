using Lifted.BlazorAuth.Basic.Models;
using Lifted.BlazorAuth.Basic.Services;
using Microsoft.Extensions.Time.Testing;
using StageZero.Tests.Fakes;

namespace StageZero.Tests.Auth;

/// <summary>
/// Six-digit codes and passwords can only be guessed a few times: per code, per account,
/// and per address. The codes go to the server log, so the per-address limit on reset
/// requests also stops one caller filling the log with banners.
/// </summary>
public class BruteForceTests
{
    private const string Email = "owner@example.com";
    private const string Password = "correct horse battery";

    private readonly FakeTimeProvider _time = new();
    private readonly InMemoryUsers _users = new();
    private readonly FakeCodeSink _codes = new();
    private readonly AuthThrottle _throttle;

    public BruteForceTests()
    {
        _throttle = new AuthThrottle(_time);
        _users.InsertAsync(new User { Email = Email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password), EmailVerified = true }).Wait();
    }

    private AuthService Caller(string address = "203.0.113.10") =>
        TestAuth.Create(_users, _codes, _time, _throttle, address);

    // ─── reset codes ────────────────────────────────────────────

    [Fact]
    public async Task The_sixth_guess_at_a_reset_code_fails_even_when_right()
    {
        var auth = Caller();
        await auth.SendPasswordResetCodeAsync(Email);
        var code = _codes.Sent.Single().Code;

        for (var guess = 0; guess < AuthService.MaxCodeAttempts; guess++)
        {
            Assert.False(await auth.ResetPasswordAsync(Email, WrongCode(code, guess), "new password 123"));
        }

        Assert.False(await auth.ResetPasswordAsync(Email, code, "new password 123"));
        Assert.Null(_users.All.Single().PasswordResetCode);
        Assert.True(BCrypt.Net.BCrypt.Verify(Password, _users.All.Single().PasswordHash));
    }

    [Fact]
    public async Task A_reset_code_survives_fewer_wrong_guesses_than_the_limit()
    {
        var auth = Caller();
        await auth.SendPasswordResetCodeAsync(Email);
        var code = _codes.Sent.Single().Code;

        for (var guess = 0; guess < AuthService.MaxCodeAttempts - 1; guess++)
        {
            await auth.ResetPasswordAsync(Email, WrongCode(code, guess), "new password 123");
        }

        Assert.True(await auth.ResetPasswordAsync(Email, code, "new password 123"));
    }

    [Fact]
    public async Task Reset_codes_are_six_digits()
    {
        await Caller().SendPasswordResetCodeAsync(Email);

        Assert.Matches("^[0-9]{6}$", _codes.Sent.Single().Code);
    }

    [Fact]
    public async Task A_reset_code_expires()
    {
        var auth = Caller();
        await auth.SendPasswordResetCodeAsync(Email);
        var code = _codes.Sent.Single().Code;

        _time.Advance(AuthService.CodeLifetime + TimeSpan.FromSeconds(1));

        Assert.False(await auth.ResetPasswordAsync(Email, code, "new password 123"));
    }

    [Fact]
    public async Task One_address_cannot_keep_requesting_reset_codes()
    {
        var auth = Caller();
        for (var i = 0; i < AuthThrottle.MaxFailures; i++)
        {
            await auth.SendPasswordResetCodeAsync(Email);
        }

        await Assert.ThrowsAsync<AuthThrottledException>(() => auth.SendPasswordResetCodeAsync(Email));
        Assert.Equal(AuthThrottle.MaxFailures, _codes.Sent.Count);

        // Another address is unaffected, and the first recovers once the window passes.
        Assert.True(await Caller("198.51.100.20").SendPasswordResetCodeAsync(Email));
        _time.Advance(AuthThrottle.Window);
        Assert.True(await auth.SendPasswordResetCodeAsync(Email));
    }

    // ─── verification codes ─────────────────────────────────────

    [Fact]
    public async Task The_sixth_guess_at_a_verification_code_fails_even_when_right()
    {
        var auth = Caller();
        Assert.True((await auth.LoginAsync(Email, Password)).Success);
        await auth.SendEmailVerificationCodeAsync(Email);
        var code = _codes.Sent.Single().Code;

        for (var guess = 0; guess < AuthService.MaxCodeAttempts; guess++)
        {
            Assert.False(await auth.VerifyEmailCodeAsync(WrongCode(code, guess)));
        }

        Assert.False(await auth.VerifyEmailCodeAsync(code));
        Assert.Null(_users.All.Single().EmailVerificationCode);
    }

    // ─── sign-in ────────────────────────────────────────────────

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_for_a_while()
    {
        for (var i = 0; i < AuthService.MaxFailedLogins; i++)
        {
            // Spread across addresses, so the per-address throttle isn't what stops them.
            await Caller($"198.51.100.{i + 1}").LoginAsync(Email, "wrong");
        }

        var locked = await Caller("192.0.2.1").LoginAsync(Email, Password);
        Assert.False(locked.Success);
        Assert.Contains("locked", locked.ErrorMessage);

        _time.Advance(AuthService.LockoutDuration);
        Assert.True((await Caller("192.0.2.1").LoginAsync(Email, Password)).Success);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_the_failure_count()
    {
        var auth = Caller();
        for (var i = 0; i < AuthService.MaxFailedLogins - 1; i++)
        {
            await auth.LoginAsync(Email, "wrong");
        }

        Assert.True((await auth.LoginAsync(Email, Password)).Success);
        Assert.Equal(0, _users.All.Single().FailedLoginCount);
    }

    [Fact]
    public async Task One_address_guessing_many_accounts_is_throttled()
    {
        var auth = Caller();
        for (var i = 0; i < AuthThrottle.MaxFailures; i++)
        {
            await auth.LoginAsync($"nobody{i}@example.com", "guess");
        }

        var refused = await auth.LoginAsync(Email, Password);
        Assert.False(refused.Success);
        Assert.Contains("Too many attempts", refused.ErrorMessage);

        Assert.True((await Caller("198.51.100.20").LoginAsync(Email, Password)).Success);
    }

    private static string WrongCode(string code, int offset) =>
        ((int.Parse(code, System.Globalization.CultureInfo.InvariantCulture) + offset + 1) % 1_000_000)
        .ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
}
