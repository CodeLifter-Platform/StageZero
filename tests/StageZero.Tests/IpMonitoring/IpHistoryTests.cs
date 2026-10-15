using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Data;
using StageZero.DataAdapters.IpChecks;
using StageZero.Services.IpMonitoring;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.IpMonitoring;

/// <summary>
/// A check every few minutes used to add a row every few minutes, forever. The history
/// now grows with changes, not with time.
/// </summary>
[Collection(AppCollection.Name)]
public class IpHistoryTests
{
    [Fact]
    public async Task A_stable_ip_extends_one_row_and_a_change_starts_the_next()
    {
        await using var app = new StageZeroApp();

        app.Http.PublicIp("203.0.113.1");
        for (var i = 0; i < 5; i++)
        {
            await CheckAsync(app);
        }

        app.Http.PublicIp("203.0.113.2");
        await CheckAsync(app);
        await CheckAsync(app);

        await using var db = await app.CreateDbContextAsync();
        var runs = await db.IpChecks.OrderBy(c => c.CheckedAt).ToListAsync();
        Assert.Equal([("203.0.113.1", 5), ("203.0.113.2", 2)], runs.Select(r => (r.IpAddress, r.Confirmations)));
        Assert.True(runs[0].LastConfirmedAt > runs[0].CheckedAt);
        Assert.Equal("203.0.113.1", runs[1].PreviousIpAddress);

        using var scope = app.Services.CreateScope();
        var grouped = await scope.ServiceProvider.GetRequiredService<IIpCheckReader>().GetGroupedByIpAsync();
        Assert.Equal(5, grouped.Single(g => g.IpAddress == "203.0.113.1").CheckCount);
    }

    [Fact]
    public async Task Upgrading_folds_the_old_history_into_runs()
    {
        var directory = Directory.CreateTempSubdirectory("stagezero-db-").FullName;
        var databasePath = Path.Combine(directory, "stagezero.db");
        try
        {
            await using (var db = DatabaseInitializer.CreateContext(databasePath))
            {
                await db.GetService<IMigrator>().MigrateAsync("StopAutoUpdatingNonARecords");
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO IpChecks (IpAddress, CheckedAt, IsChanged, PreviousIpAddress) VALUES
                        ('203.0.113.1', '2026-01-01 00:00:00', 1, NULL),
                        ('203.0.113.1', '2026-01-01 00:03:00', 0, NULL),
                        ('203.0.113.1', '2026-01-01 00:06:00', 0, NULL),
                        ('203.0.113.2', '2026-01-01 00:09:00', 1, '203.0.113.1'),
                        ('203.0.113.2', '2026-01-01 00:12:00', 0, NULL),
                        ('203.0.113.1', '2026-01-01 00:15:00', 1, '203.0.113.2');
                    """);
            }

            await DatabaseInitializer.InitializeAsync(databasePath, NullLogger.Instance);

            await using var upgraded = DatabaseInitializer.CreateContext(databasePath);
            var runs = await upgraded.IpChecks.OrderBy(c => c.CheckedAt).ToListAsync();
            Assert.Equal(
                [("203.0.113.1", 3, "00:06"), ("203.0.113.2", 2, "00:12"), ("203.0.113.1", 1, "00:15")],
                runs.Select(r => (r.IpAddress, r.Confirmations, r.LastConfirmedAt.ToString("HH:mm"))));
            Assert.Equal("00:00", runs[0].CheckedAt.ToString("HH:mm"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CheckAsync(StageZeroApp app)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IIpMonitorService>().CheckIpAsync();
    }
}
