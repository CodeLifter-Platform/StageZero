using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Data;
using StageZero.Models;
using StageZero.Services.Cloudflare;
using StageZero.Tests.Dns;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Data;

/// <summary>
/// A copy of stagezero.db — a backup, a leaked volume — must not hand over the Cloudflare
/// token that can rewrite every DNS record and tunnel route on the account.
/// </summary>
[Collection(AppCollection.Name)]
public class CloudflareTokenStorageTests
{
    private const string Token = CloudflareFakes.Token;

    [Fact]
    public async Task The_saved_token_is_stored_encrypted_and_read_back_in_the_clear()
    {
        await using var app = new StageZeroApp();
        await CloudflareFakes.SaveTokenAsync(app, "account-1");

        using var scope = app.Services.CreateScope();
        var resolved = await scope.ServiceProvider.GetRequiredService<ICloudflareCredentialService>().ResolveAsync();

        Assert.DoesNotContain(Token, await DatabaseTextAsync(app));
        Assert.Equal(new CloudflareCredentials(Token, "account-1"), resolved);
    }

    [Fact]
    public async Task A_token_stored_in_the_clear_is_encrypted_at_startup()
    {
        await using var app = new StageZeroApp();
        await DatabaseInitializer.InitializeAsync(app.Database, NullLogger.Instance);
        await using (var db = app.Database.CreateContext())
        {
            // What the migration carries over from a DNS provider saved before tokens were encrypted.
            db.CloudflareCredentials.Add(new CloudflareCredential { ProtectedApiToken = Token });
            await db.SaveChangesAsync();
        }

        using var scope = app.Services.CreateScope(); // starts the app

        Assert.DoesNotContain(Token, await DatabaseTextAsync(app));
        var resolved = await scope.ServiceProvider.GetRequiredService<ICloudflareCredentialService>().ResolveAsync();
        Assert.Equal(Token, resolved?.ApiToken);
    }

    [Theory]
    [InlineData(true, "CfDJ8-tunnel", "account-1")]
    [InlineData(false, "CfDJ8-enabled-provider", null)]
    public async Task Upgrading_keeps_one_token_preferring_the_tunnels(bool withTunnel, string expectedToken, string? expectedAccount)
    {
        var directory = Directory.CreateTempSubdirectory("stagezero-db-").FullName;
        var databasePath = Path.Combine(directory, "stagezero.db");
        try
        {
            await using (var db = DatabaseInitializer.CreateContext(databasePath))
            {
                // The schema as it was just before one token replaced the per-zone and tunnel ones.
                await db.GetService<IMigrator>().MigrateAsync("AddSignInLockout");
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO DnsProviders (Id, Name, ProviderType, ProtectedApiToken, ZoneId, IsActive, CreatedAt) VALUES
                        (1, 'Off', 'Cloudflare', 'CfDJ8-disabled-provider', 'z1', 0, '2026-01-02 03:04:05'),
                        (2, 'On', 'Cloudflare', 'CfDJ8-enabled-provider', 'z2', 1, '2026-01-02 03:04:05');
                    """);
                if (withTunnel)
                {
                    await db.Database.ExecuteSqlRawAsync("""
                        INSERT INTO TunnelConfigs (CloudflareAccountId, CloudflareZoneId, ProtectedApiToken, TunnelId, UpdatedAt)
                            VALUES ('account-1', 'z2', 'CfDJ8-tunnel', 't-1', '2026-01-02 03:04:05');
                        """);
                }
            }

            await DatabaseInitializer.InitializeAsync(databasePath, NullLogger.Instance);

            await using var upgraded = DatabaseInitializer.CreateContext(databasePath);
            var credential = await upgraded.CloudflareCredentials.SingleAsync();
            Assert.Equal((expectedToken, expectedAccount), (credential.ProtectedApiToken, credential.AccountId));
            Assert.Equal(2, await upgraded.DnsProviders.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// What a copy of the database would hold, as text to search for a secret: the whole SQLite
    /// file (WAL folded in), or on PostgreSQL the stored token.
    /// </summary>
    private static async Task<string> DatabaseTextAsync(StageZeroApp app)
    {
        if (app.IsPostgres)
        {
            await using var db = app.Database.CreateContext();
            var stored = await db.Database
                .SqlQueryRaw<string>("SELECT \"ProtectedApiToken\" AS \"Value\" FROM \"CloudflareCredentials\"")
                .ToListAsync();
            return string.Join("\n", stored);
        }

        var databasePath = Path.Combine(app.DataDirectory, "stagezero.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await checkpoint.ExecuteNonQueryAsync();
        }

        await using var file = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(file, Encoding.Latin1);
        return await reader.ReadToEndAsync();
    }
}
