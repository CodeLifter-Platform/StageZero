using Microsoft.EntityFrameworkCore;
using Lifted.BlazorAuth.Basic.Data;
using Lifted.BlazorAuth.Basic.Models;

namespace Lifted.BlazorAuth.Basic.DataAdapters;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IUserReader
{
    Task<User?> GetByIdAsync(int id);
    Task<User?> GetByEmailAsync(string email);
    Task<List<User>> GetAllAsync();
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class UserReader : IUserReader
{
    private readonly IDbContextFactory<BasicAuthDbContext> _factory;

    public UserReader(IDbContextFactory<BasicAuthDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Users.FindAsync(id);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<List<User>> GetAllAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .ToListAsync();
    }
}

