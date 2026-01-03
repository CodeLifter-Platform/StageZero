using Microsoft.EntityFrameworkCore;
using Lifted.BlazorAuth.Basic.Data;
using Lifted.BlazorAuth.Basic.Models;

namespace Lifted.BlazorAuth.Basic.DataAdapters;

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
    private readonly IDbContextFactory<BasicAuthDbContext> _factory;

    public UserWriter(IDbContextFactory<BasicAuthDbContext> factory)
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

