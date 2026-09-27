using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Data;

namespace StageZero.Services.Cli;

/// <summary>
/// <c>StageZero reset-password &lt;email&gt;</c>: account recovery from the server itself, for
/// when the logged reset code is out of reach (the log is gone, or the address is forgotten).
/// Prints a one-time password; the next sign-in must replace it. It also lifts any lockout
/// and voids any outstanding reset code.
/// </summary>
public static class ResetPasswordCommand
{
    public const string Name = "reset-password";

    // No look-alikes (0/O, 1/l/I), so it survives being read off a terminal and retyped.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static async Task<int> RunAsync(string databasePath, string[] args, TextWriter output)
    {
        if (args.Length != 2)
        {
            await output.WriteLineAsync($"Usage: StageZero {Name} <email>");
            return 2;
        }

        var email = args[1];
        await DatabaseInitializer.InitializeAsync(databasePath, NullLogger.Instance);
        await using var db = DatabaseInitializer.CreateContext(databasePath);

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            var known = await db.Users.Select(u => u.Email).OrderBy(e => e).ToListAsync();
            await output.WriteLineAsync($"No account with the email {email}.");
            await output.WriteLineAsync(known.Count == 0
                ? "There are no accounts yet: open the app and complete /setup."
                : $"Accounts: {string.Join(", ", known)}");
            return 1;
        }

        var password = RandomNumberGenerator.GetString(Alphabet, 16);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        user.RequiresPasswordChange = true;
        user.IsActive = true;
        user.PasswordResetCode = null;
        user.PasswordResetCodeExpiry = null;
        user.PasswordResetAttempts = 0;
        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        await db.SaveChangesAsync();

        await output.WriteLineAsync($"Password for {user.Email} reset to: {password}");
        await output.WriteLineAsync("Sign in with it; you'll be asked to choose a new one.");
        return 0;
    }
}
