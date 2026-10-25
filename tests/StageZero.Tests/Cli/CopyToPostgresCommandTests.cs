using Lifted.BlazorAuth.Basic.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StageZero.Data;
using StageZero.Models;
using StageZero.Services.Cli;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Cli;

public sealed class CopyToPostgresCommandTests : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("stagezero-copy-").FullName;
    private readonly string? _target;

    public CopyToPostgresCommandTests()
    {
        var server = Environment.GetEnvironmentVariable(StageZeroApp.PostgresServerVariable);
        if (!string.IsNullOrWhiteSpace(server))
        {
            _target = new NpgsqlConnectionStringBuilder(server) { Database = $"stagezero_copy_{Guid.NewGuid():N}" }.ToString();
        }
    }

    private string SqlitePath => Path.Combine(_directory, "stagezero.db");

    [Fact]
    public void Every_table_in_the_model_is_copied()
    {
        using var db = DatabaseInitializer.CreateContext(SqlitePath);
        var tables = db.Model.GetEntityTypes().Where(e => !e.IsOwned()).Select(e => e.ClrType);

        Assert.Equal(tables.OrderBy(t => t.Name), CopyToPostgresCommand.CopiedTypes.OrderBy(t => t.Name));
    }

    [Fact]
    public async Task Without_a_target_it_explains_itself_and_changes_nothing()
    {
        using var output = new StringWriter();

        var exitCode = await CopyToPostgresCommand.RunAsync(SqlitePath, null, output);

        Assert.Equal(2, exitCode);
        Assert.Contains(DatabaseSettings.ConnectionVariable, output.ToString());
        Assert.False(File.Exists(SqlitePath));
    }

    [PostgresFact]
    public async Task Every_row_moves_across_with_its_id_and_its_token_still_encrypted()
    {
        await SeedSqliteAsync();
        using var output = new StringWriter();

        var exitCode = await CopyToPostgresCommand.RunAsync(SqlitePath, _target, output);

        Assert.True(exitCode == 0, output.ToString());
        await using var db = DatabaseSettings.Postgres(_target!).CreateContext();
        Assert.Equal("owner@example.com", (await db.Users.SingleAsync()).Email);
        Assert.Equal("300", (await db.AppSettings.SingleAsync()).Value);
        Assert.Equal(7, (await db.DnsProviders.SingleAsync()).Id);
        var credential = await db.CloudflareCredentials.SingleAsync();
        Assert.Equal(("CfDJ8-protected-token", "acct-1"), (credential.ProtectedApiToken, credential.AccountId));
        var record = await db.DnsRecords.SingleAsync();
        Assert.Equal((7, "home.example.com"), (record.DnsProviderId, record.RecordName));
        var check = await db.IpChecks.SingleAsync();
        Assert.Equal((42, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)), (check.Confirmations, check.LastConfirmedAt));
        Assert.Equal("t-1", (await db.TunnelConfigs.SingleAsync()).TunnelId);
        var route = await db.TunnelRoutes.SingleAsync();
        Assert.Equal(("app.example.com", "owner@example.com"), (route.DomainName, route.Access.AllowedEmails));
        Assert.Equal("token-1", (await db.AccessServiceTokens.SingleAsync()).CloudflareTokenId);

        // The sequences moved past the copied ids: a new row doesn't collide.
        db.DnsProviders.Add(new DnsProvider { Name = "Second", ProviderType = "Cloudflare" });
        await db.SaveChangesAsync();
        Assert.Equal(8, (await db.DnsProviders.OrderBy(p => p.Id).LastAsync()).Id);
    }

    [PostgresFact]
    public async Task A_database_that_already_has_data_is_left_alone()
    {
        await SeedSqliteAsync();
        await CopyToPostgresCommand.RunAsync(SqlitePath, _target, TextWriter.Null);
        using var output = new StringWriter();

        var exitCode = await CopyToPostgresCommand.RunAsync(SqlitePath, _target, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("already has data", output.ToString());
        await using var db = DatabaseSettings.Postgres(_target!).CreateContext();
        Assert.Equal(1, await db.DnsProviders.CountAsync());
    }

    // ───────────────────────────────────────────────────────────

    private async Task SeedSqliteAsync()
    {
        await DatabaseInitializer.InitializeAsync(SqlitePath, NullLogger.Instance);
        await using var db = DatabaseInitializer.CreateContext(SqlitePath);
        db.Users.Add(new User { Email = "owner@example.com", PasswordHash = "hash", EmailVerified = true, IsActive = true });
        db.AppSettings.Add(new AppSettings { Key = "CheckIntervalSeconds", Value = "300" });
        db.DnsProviders.Add(new DnsProvider
        {
            Id = 7, Name = "Home", ProviderType = "Cloudflare", ZoneId = "zone-1",
            DnsRecords = [new DnsRecord { RecordName = "home.example.com", RecordType = "A", AutoUpdate = true }]
        });
        db.IpChecks.Add(new IpCheck
        {
            IpAddress = "203.0.113.7", CheckedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            LastConfirmedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), Confirmations = 42
        });
        db.CloudflareCredentials.Add(new CloudflareCredential { ProtectedApiToken = "CfDJ8-protected-token", AccountId = "acct-1" });
        db.TunnelConfigs.Add(new TunnelConfig { TunnelId = "t-1" });
        db.TunnelRoutes.Add(new TunnelRoute
        {
            DomainName = "app.example.com", ForwardHost = "192.168.1.10", ForwardPort = 8080,
            Access = new RouteAccessSettings { AllowedEmails = "owner@example.com" }
        });
        db.AccessServiceTokens.Add(new AccessServiceToken { CloudflareTokenId = "token-1", Name = "ci" });
        await db.SaveChangesAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
        if (_target is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        var target = new NpgsqlConnectionStringBuilder(_target);
        var maintenance = new NpgsqlConnectionStringBuilder(_target) { Database = "postgres", Pooling = false };
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{target.Database}\" WITH (FORCE);";
        await drop.ExecuteNonQueryAsync();
    }
}
