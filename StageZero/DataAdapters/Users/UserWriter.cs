using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.Users;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IUserWriter
{
    Task<User> InsertAsync(User user);
    Task UpdateAsync(User user);
    Task DeleteAsync(User user);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class UserWriter : IUserWriter
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public UserWriter(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<User> InsertAsync(User user)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task UpdateAsync(User user)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Users.Update(user);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(User user)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }
}

