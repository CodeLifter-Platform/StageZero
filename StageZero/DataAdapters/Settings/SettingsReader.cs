using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.Settings;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface ISettingsReader
{
    Task<string?> GetValueAsync(string key);
    Task<int> GetIntValueAsync(string key, int defaultValue = 0);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class SettingsReader : ISettingsReader
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public SettingsReader(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<string?> GetValueAsync(string key)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var setting = await db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key);
        return setting?.Value;
    }

    public async Task<int> GetIntValueAsync(string key, int defaultValue = 0)
    {
        var value = await GetValueAsync(key);
        return int.TryParse(value, out var result) ? result : defaultValue;
    }
}

