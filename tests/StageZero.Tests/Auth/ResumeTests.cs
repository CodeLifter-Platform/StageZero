using Lifted.BlazorAuth.Basic.Models;
using Lifted.BlazorAuth.Basic.Services;
using Microsoft.Extensions.Time.Testing;
using StageZero.Tests.Fakes;

namespace StageZero.Tests.Auth;

/// <summary>
/// <see cref="IAuthService.ResumeAsync"/> is how a host that keeps its own session (a cookie)
/// makes the user current for one request. It must refuse anyone who could not sign in now.
/// </summary>
public class ResumeTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly InMemoryUsers _users = new();

    private async Task<User> AddAsync(Action<User>? change = null)
    {
        var user = new User { Email = "owner@example.com", PasswordHash = "x", IsActive = true, EmailVerified = true };
        change?.Invoke(user);
        return await _users.InsertAsync(user);
    }

    private AuthService Auth() => TestAuth.Create(_users, new FakeCodeSink(), _time);

    [Fact]
    public async Task An_active_user_becomes_current()
    {
        var user = await AddAsync();
        var auth = Auth();

        Assert.True(await auth.ResumeAsync(user.Id));
        Assert.True(auth.IsAuthenticated);
        Assert.Equal("owner@example.com", (await auth.GetCurrentUserAsync())?.Email);
    }

    [Fact]
    public async Task Resuming_carries_what_the_account_still_owes()
    {
        var user = await AddAsync(u => { u.RequiresPasswordChange = true; u.EmailVerified = false; });
        var auth = Auth();

        await auth.ResumeAsync(user.Id);

        Assert.True(auth.RequiresPasswordChange);
        Assert.True(auth.RequiresEmailVerification);
    }

    [Fact]
    public async Task A_deleted_inactive_or_locked_account_is_refused()
    {
        var inactive = await AddAsync(u => u.IsActive = false);
        var locked = await AddAsync(u => u.LockoutEndsAt = _time.GetUtcNow().UtcDateTime.AddMinutes(5));
        var auth = Auth();

        Assert.False(await auth.ResumeAsync(999));
        Assert.False(await auth.ResumeAsync(inactive.Id));
        Assert.False(await auth.ResumeAsync(locked.Id));
        Assert.False(auth.IsAuthenticated);
    }

    [Fact]
    public async Task A_lockout_that_has_run_out_does_not_refuse()
    {
        var user = await AddAsync(u => u.LockoutEndsAt = _time.GetUtcNow().UtcDateTime.AddMinutes(-1));

        Assert.True(await Auth().ResumeAsync(user.Id));
    }
}
