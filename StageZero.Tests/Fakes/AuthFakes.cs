using Lifted.BlazorAuth.Basic.DataAdapters;
using Lifted.BlazorAuth.Basic.Models;
using Lifted.BlazorAuth.Basic.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace StageZero.Tests.Fakes;

/// <summary>Records every code the auth service would have delivered.</summary>
internal sealed class FakeCodeSink : IEmailService
{
    public List<(string To, string Code)> Sent { get; } = [];

    public Task SendVerificationCodeAsync(string toEmail, string code)
    {
        Sent.Add((toEmail, code));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(string toEmail, string code)
    {
        Sent.Add((toEmail, code));
        return Task.CompletedTask;
    }

    public Task<bool> IsConfiguredAsync() => Task.FromResult(false);
}

/// <summary>A users table in a list. Hands out copies, as a real DbContext-per-call would.</summary>
internal sealed class InMemoryUsers : IUserReader, IUserWriter
{
    private readonly List<User> _users = [];
    private int _nextId = 1;

    public IReadOnlyList<User> All => _users;

    public Task<User?> GetByIdAsync(int id) => Task.FromResult(Copy(_users.FirstOrDefault(u => u.Id == id)));

    public Task<User?> GetByEmailAsync(string email) =>
        Task.FromResult(Copy(_users.FirstOrDefault(u => u.Email == email)));

    public Task<List<User>> GetAllAsync() => Task.FromResult(_users.Select(u => Copy(u)!).ToList());

    public Task<User> InsertAsync(User user)
    {
        user.Id = _nextId++;
        _users.Add(Copy(user)!);
        return Task.FromResult(user);
    }

    public Task UpdateAsync(User user)
    {
        var index = _users.FindIndex(u => u.Id == user.Id);
        _users[index] = Copy(user)!;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(User user)
    {
        _users.RemoveAll(u => u.Id == user.Id);
        return Task.CompletedTask;
    }

    private static User? Copy(User? user) => user is null ? null : (User)user.GetType()
        .GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(user, null)!;
}

/// <summary>
/// Unlike the real accessor (one static AsyncLocal), each instance keeps its own context, so
/// one test can play callers at two addresses.
/// </summary>
internal sealed class FixedHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}

internal static class TestAuth
{
    /// <summary>A real AuthService over fakes, for a caller at <paramref name="address"/>.</summary>
    public static AuthService Create(
        InMemoryUsers users,
        IEmailService codes,
        TimeProvider? time = null,
        IAuthThrottle? throttle = null,
        string address = "203.0.113.10")
    {
        time ??= TimeProvider.System;
        var accessor = new FixedHttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                Connection = { RemoteIpAddress = System.Net.IPAddress.Parse(address) }
            }
        };

        return new AuthService(
            NullLogger<AuthService>.Instance,
            users, users, codes,
            throttle ?? new AuthThrottle(time), new ClientAddress(accessor), time);
    }
}
