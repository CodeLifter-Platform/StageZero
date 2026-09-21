using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Data;
using StageZero.DataAdapters.DnsProviders;
using StageZero.DataAdapters.DnsRecords;
using StageZero.Models;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Data;

/// <summary>
/// A copy of stagezero.db — a backup, a leaked volume — must not hand over the Cloudflare
/// token that can rewrite every DNS record in the zone.
/// </summary>
[Collection(AppCollection.Name)]
public class DnsTokenStorageTests
{
    private const string Token = "cf-test-token-0123456789abcdefghijklmnop";

    [Fact]
    public async Task A_new_provider_token_is_stored_encrypted_and_read_back_in_the_clear()
    {
        await using var app = new StageZeroApp();
        using var scope = app.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IDnsProviderWriter>();

        var provider = await writer.InsertAsync(new DnsProvider
        {
            Name = "Home", ProviderType = "Cloudflare", ApiToken = Token, ZoneId = "zone-1"
        });
        await scope.ServiceProvider.GetRequiredService<IDnsRecordWriter>().InsertAsync(new DnsRecord
        {
            DnsProviderId = provider.Id, RecordName = "home.example.com", RecordType = "A", AutoUpdate = true
        });

        Assert.DoesNotContain(Token, await DatabaseTextAsync(app));
        Assert.Equal(Token, (await scope.ServiceProvider.GetRequiredService<IDnsProviderReader>().GetAllAsync()).Single().ApiToken);
        var record = (await scope.ServiceProvider.GetRequiredService<IDnsRecordReader>().GetAutoUpdateRecordsAsync()).Single();
        Assert.Equal(Token, record.DnsProvider.ApiToken);
    }

    [Fact]
    public async Task A_token_stored_in_the_clear_before_this_is_encrypted_at_startup()
    {
        await using var app = new StageZeroApp();
        var databasePath = Path.Combine(app.DataDirectory, "stagezero.db");
        await DatabaseInitializer.InitializeAsync(databasePath, NullLogger.Instance);
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText = $"""
                INSERT INTO DnsProviders (Name, ProviderType, ProtectedApiToken, ZoneId, IsActive, CreatedAt)
                VALUES ('Home', 'Cloudflare', '{Token}', 'zone-1', 1, '2026-01-02 03:04:05');
                """;
            await insert.ExecuteNonQueryAsync();
        }

        using var scope = app.Services.CreateScope(); // starts the app

        Assert.DoesNotContain(Token, await DatabaseTextAsync(app));
        Assert.Equal(Token, (await scope.ServiceProvider.GetRequiredService<IDnsProviderReader>().GetAllAsync()).Single().ApiToken);
    }

    /// <summary>The whole database file (WAL folded in) as text, to search for a secret.</summary>
    private static async Task<string> DatabaseTextAsync(StageZeroApp app)
    {
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
