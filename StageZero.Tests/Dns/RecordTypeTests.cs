using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Application.Areas.DnsConfig;
using StageZero.Data;
using StageZero.Models;
using StageZero.Services.IpMonitoring;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Dns;

/// <summary>
/// Only A records get the (IPv4) public IP. An IPv4 address written into an AAAA record is
/// refused by Cloudflare on every check, so the UI must not offer it and the verifier must
/// not try.
/// </summary>
[Collection(AppCollection.Name)]
public class RecordTypeTests
{
    [Fact]
    public async Task Verification_updates_the_A_record_and_leaves_the_AAAA_record_alone()
    {
        await using var app = new StageZeroApp();
        await CloudflareFakes.AddProviderAsync(app,
            new DnsRecord { RecordName = "home.example.com", RecordType = "A", RecordId = "rec-a", AutoUpdate = true },
            new DnsRecord { RecordName = "home.example.com", RecordType = "AAAA", RecordId = "rec-aaaa", AutoUpdate = true });
        app.Http.PublicIp("203.0.113.2");
        app.Http.OnGet(CloudflareFakes.ZoneUrl, CloudflareFakes.List(
            CloudflareFakes.Record("rec-a", "home.example.com", "A", "203.0.113.1"),
            CloudflareFakes.Record("rec-aaaa", "home.example.com", "AAAA", "2001:db8::1")));
        CloudflareFakes.AcceptUpdates(app);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIpMonitorService>().CheckIpAsync();
        }

        var patch = Assert.Single(app.Http.Requests, r => r.Method == HttpMethod.Patch);
        Assert.EndsWith("/rec-a", patch.Url.ToString());
    }

    [Theory]
    [InlineData("A", true, true)]
    [InlineData("A", false, false)] // the dialog's checkbox used to be ignored
    [InlineData("AAAA", true, false)]
    [InlineData("CNAME", true, false)]
    public async Task Adding_a_record_respects_the_checkbox_and_the_type(string type, bool ticked, bool stored)
    {
        await using var app = new StageZeroApp();
        var provider = await CloudflareFakes.AddProviderAsync(app);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDnsConfigViewModel>()
                .AddRecordAsync(provider.Id, "home.example.com", type, ticked);
        }

        await using var db = await app.CreateDbContextAsync();
        Assert.Equal(stored, (await db.DnsRecords.SingleAsync()).AutoUpdate);
    }

    [Fact]
    public async Task Upgrading_switches_off_auto_update_on_existing_non_A_records()
    {
        var directory = Directory.CreateTempSubdirectory("stagezero-db-").FullName;
        var databasePath = Path.Combine(directory, "stagezero.db");
        try
        {
            await using (var db = DatabaseInitializer.CreateContext(databasePath))
            {
                // The schema as it was just before this migration.
                await db.GetService<IMigrator>().MigrateAsync("ProtectDnsProviderToken");
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO DnsProviders (Id, Name, ProviderType, ProtectedApiToken, IsActive, CreatedAt)
                        VALUES (1, 'Home', 'Cloudflare', 'x', 1, '2026-01-02 03:04:05');
                    INSERT INTO DnsRecords (DnsProviderId, RecordName, RecordType, AutoUpdate, CreatedAt) VALUES
                        (1, 'a.example.com', 'A', 1, '2026-01-02 03:04:05'),
                        (1, 'v6.example.com', 'AAAA', 1, '2026-01-02 03:04:05'),
                        (1, 'www.example.com', 'CNAME', 1, '2026-01-02 03:04:05');
                    """);
            }

            await DatabaseInitializer.InitializeAsync(databasePath, NullLogger.Instance);

            await using var upgraded = DatabaseInitializer.CreateContext(databasePath);
            var flags = await upgraded.DnsRecords.OrderBy(r => r.RecordName)
                .Select(r => new { r.RecordType, r.AutoUpdate }).ToListAsync();
            Assert.Equal([("A", true), ("AAAA", false), ("CNAME", false)],
                flags.Select(f => (f.RecordType, f.AutoUpdate)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
