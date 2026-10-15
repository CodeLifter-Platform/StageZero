using Lifted.BlazorAuth.Basic.Models;

namespace StageZero.Tests.Infrastructure;

internal static class TestData
{
    public static async Task<User> AddUserAsync(StageZeroApp app, string email, string password)
    {
        // Touching Services starts the host, which creates the database.
        await using var db = await app.CreateDbContextAsync();
        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            EmailVerified = true,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
