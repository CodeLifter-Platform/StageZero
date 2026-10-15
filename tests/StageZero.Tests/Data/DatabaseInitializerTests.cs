using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Data;

namespace StageZero.Tests.Data;

public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("stagezero-db-").FullName;
    private string DatabasePath => Path.Combine(_directory, "stagezero.db");

    [Fact]
    public async Task A_fresh_install_is_created_by_the_migrations()
    {
        await InitializeAsync();

        Assert.Contains(DatabaseInitializer.BaselineMigration, await AppliedMigrationsAsync());
        Assert.Contains("TunnelRoutes", await TablesAsync(DatabasePath));
        Assert.Empty(Backups());
    }

    [Fact]
    public async Task A_june_2026_database_is_adopted_with_its_data()
    {
        await CreateLegacyAsync(LegacySchemas.June2026, LegacySchemas.CommonRows + """
            INSERT INTO Users VALUES (1, 'owner@example.com', 'hash', 1, NULL, NULL, NULL, NULL,
                '2026-01-02 03:04:05', NULL, 1, 0);
            """);

        await InitializeAsync();

        Assert.Contains(DatabaseInitializer.BaselineMigration, await AppliedMigrationsAsync());
        await using var db = DatabaseInitializer.CreateContext(DatabasePath);
        var record = await db.DnsRecords.Include(r => r.DnsProvider).SingleAsync();
        Assert.Equal("home.example.com", record.RecordName);
        Assert.Equal("203.0.113.7", record.LastIpAddress);
        Assert.Equal("test-token", record.DnsProvider.ProtectedApiToken); // encrypted later, at app startup
        Assert.Equal("owner@example.com", (await db.Users.SingleAsync()).Email);
        Assert.Equal("300", (await db.AppSettings.SingleAsync()).Value);
        Assert.Equal(1, await db.IpChecks.CountAsync());
        Assert.Empty(await db.TunnelRoutes.ToListAsync());

        var tables = await TablesAsync(DatabasePath);
        Assert.DoesNotContain("ProxyHosts", tables);

        // The original survives untouched beside the adopted database.
        var backup = Assert.Single(Backups());
        Assert.Contains("ProxyHosts", await TablesAsync(backup));
    }

    [Fact]
    public async Task A_january_2026_user_keyed_on_username_signs_in_by_email()
    {
        await CreateLegacyAsync(LegacySchemas.January2026, LegacySchemas.CommonRows + """
            INSERT INTO Users VALUES (1, 'owner@example.com', 'hash', NULL, 1, NULL, NULL,
                '2026-01-02 03:04:05', NULL, 1, 0, NULL, NULL);
            """);

        await InitializeAsync();

        await using var db = DatabaseInitializer.CreateContext(DatabasePath);
        Assert.Equal("owner@example.com", (await db.Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task A_migrated_database_is_left_alone()
    {
        await InitializeAsync();
        await using (var db = DatabaseInitializer.CreateContext(DatabasePath))
        {
            db.AppSettings.Add(new StageZero.Models.AppSettings { Key = "k", Value = "v" });
            await db.SaveChangesAsync();
        }

        await InitializeAsync();

        Assert.Empty(Backups());
        await using var reopened = DatabaseInitializer.CreateContext(DatabasePath);
        Assert.Equal("v", (await reopened.AppSettings.SingleAsync()).Value);
    }

    [Fact]
    public async Task A_database_that_cannot_be_adopted_is_left_exactly_as_it_was()
    {
        // Two usernames sharing an email cannot both survive the unique Email index.
        await CreateLegacyAsync(LegacySchemas.January2026, """
            INSERT INTO Users VALUES (1, 'alice', 'hash', 'same@example.com', 1, NULL, NULL,
                '2026-01-02 03:04:05', NULL, 1, 0, NULL, NULL);
            INSERT INTO Users VALUES (2, 'bob', 'hash', 'same@example.com', 1, NULL, NULL,
                '2026-01-02 03:04:05', NULL, 1, 0, NULL, NULL);
            """);
        var before = await File.ReadAllBytesAsync(DatabasePath);

        await Assert.ThrowsAsync<SqliteException>(InitializeAsync);

        Assert.Equal(before, await File.ReadAllBytesAsync(DatabasePath));
        Assert.Empty(Backups());
        Assert.False(File.Exists(DatabasePath + ".adopting"));
    }

    // ───────────────────────────────────────────────────────────

    private Task InitializeAsync() =>
        DatabaseInitializer.InitializeAsync(DatabasePath, NullLogger.Instance);

    private async Task CreateLegacyAsync(string schema, string rows)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = schema + rows;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> AppliedMigrationsAsync()
    {
        await using var db = DatabaseInitializer.CreateContext(DatabasePath);
        return (await db.Database.GetAppliedMigrationsAsync()).ToList();
    }

    private static async Task<List<string>> TablesAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False;Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private string[] Backups() => Directory.GetFiles(_directory, "stagezero.db.pre-migrations-*.bak");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
