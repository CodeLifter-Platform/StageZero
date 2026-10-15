using System.Text.RegularExpressions;
using Lifted.BlazorAuth.Basic.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Data;
using StageZero.Services.Cli;

namespace StageZero.Tests.Cli;

/// <summary>`StageZero reset-password &lt;email&gt;`: recovery from the server when the logged code is out of reach.</summary>
public sealed partial class ResetPasswordCommandTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("stagezero-cli-").FullName;
    private string DatabasePath => Path.Combine(_directory, "stagezero.db");

    [Fact]
    public async Task Resets_the_password_forces_a_change_and_lifts_the_lockout()
    {
        await AddUserAsync("owner@example.com", lockedOut: true);
        using var output = new StringWriter();

        var exitCode = await ResetPasswordCommand.RunAsync(
            DatabasePath, [ResetPasswordCommand.Name, "owner@example.com"], output);

        Assert.Equal(0, exitCode);
        var password = PrintedPassword().Match(output.ToString()).Groups[1].Value;
        Assert.Equal(16, password.Length);

        await using var db = DatabaseInitializer.CreateContext(DatabasePath);
        var after = await db.Users.SingleAsync();
        Assert.True(BCrypt.Net.BCrypt.Verify(password, after.PasswordHash));
        Assert.True(after.RequiresPasswordChange);
        Assert.Null(after.LockoutEndsAt);
        Assert.Null(after.PasswordResetCode);
    }

    [Fact]
    public async Task An_unknown_email_changes_nothing_and_lists_the_accounts()
    {
        var before = await AddUserAsync("owner@example.com");
        using var output = new StringWriter();

        var exitCode = await ResetPasswordCommand.RunAsync(
            DatabasePath, [ResetPasswordCommand.Name, "someone@example.com"], output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Accounts: owner@example.com", output.ToString());
        await using var db = DatabaseInitializer.CreateContext(DatabasePath);
        Assert.Equal(before.PasswordHash, (await db.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task Without_an_email_it_prints_usage()
    {
        using var output = new StringWriter();

        var exitCode = await ResetPasswordCommand.RunAsync(DatabasePath, [ResetPasswordCommand.Name], output);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage:", output.ToString());
    }

    private async Task<User> AddUserAsync(string email, bool lockedOut = false)
    {
        await DatabaseInitializer.InitializeAsync(DatabasePath, NullLogger.Instance);
        await using var db = DatabaseInitializer.CreateContext(DatabasePath);
        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("old password"),
            PasswordResetCode = "123456",
            PasswordResetCodeExpiry = DateTime.UtcNow.AddMinutes(10),
            LockoutEndsAt = lockedOut ? DateTime.UtcNow.AddMinutes(10) : null,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [GeneratedRegex("reset to: (\\S+)")]
    private static partial Regex PrintedPassword();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
