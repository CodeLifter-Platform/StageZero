using Microsoft.EntityFrameworkCore;
using StageZero.Data;
using StageZero.Models;

namespace StageZero.DataAdapters.Settings;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface ISettingsWriter
{
    Task SetValueAsync(string key, string value);
    Task SetIntValueAsync(string key, int value);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class SettingsWriter : ISettingsWriter
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public SettingsWriter(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task SetValueAsync(string key, string value)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        
        if (setting == null)
        {
            setting = new AppSettings { Key = key, Value = value };
            db.AppSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
            db.AppSettings.Update(setting);
        }
        
        await db.SaveChangesAsync();
    }

    public async Task SetIntValueAsync(string key, int value)
    {
        await SetValueAsync(key, value.ToString());
    }
}

