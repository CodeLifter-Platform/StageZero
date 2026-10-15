using Microsoft.Extensions.DependencyInjection;
using StageZero.Models;
using StageZero.Services.Dns;
using StageZero.Services.IpMonitoring;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Dns;

/// <summary>
/// Cloudflare pages its lists. Stopping at page one hid every zone past the 20th and every
/// record past the 100th, and updates for those quietly stopped.
/// </summary>
[Collection(AppCollection.Name)]
public class PaginationTests
{
    [Fact]
    public async Task A_record_on_page_two_of_a_150_record_zone_is_kept_in_sync()
    {
        await using var app = new StageZeroApp();
        await CloudflareFakes.AddProviderAsync(app,
            new DnsRecord { RecordName = "home.example.com", RecordType = "A", RecordId = "rec-home", AutoUpdate = true },
            new DnsRecord { RecordName = "nas.example.com", RecordType = "A", RecordId = "rec-nas", AutoUpdate = true },
            new DnsRecord { RecordName = "vpn.example.com", RecordType = "A", RecordId = "rec-vpn", AutoUpdate = true });
        app.Http.PublicIp("203.0.113.2");

        var filler = Enumerable.Range(0, 148)
            .Select(i => CloudflareFakes.Record($"rec-{i}", $"host{i}.example.com", "A", "198.51.100.1"))
            .ToArray();
        app.Http.OnGet($"{CloudflareFakes.ZoneUrl}?page=1&", CloudflareFakes.Page(1, 2, filler[..100]));
        app.Http.OnGet($"{CloudflareFakes.ZoneUrl}?page=2&", CloudflareFakes.Page(2, 2,
        [
            .. filler[100..],
            CloudflareFakes.Record("rec-home", "home.example.com", "A", "203.0.113.1"),
            CloudflareFakes.Record("rec-nas", "nas.example.com", "A", "203.0.113.2"), // already right
        ]));
        CloudflareFakes.AcceptUpdates(app);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIpMonitorService>().CheckIpAsync();
        }

        var patch = Assert.Single(app.Http.Requests, r => r.Method == HttpMethod.Patch);
        Assert.EndsWith("/rec-home", patch.Url.ToString());

        // Three tracked records, one zone: the zone is listed once (two pages), not three times.
        Assert.Equal(2, app.Http.Requests.Count(r => r.Method == HttpMethod.Get && r.Url.AbsolutePath.EndsWith("/dns_records")));
    }

    [Fact]
    public async Task Every_zone_is_listed_not_just_the_first_page()
    {
        await using var app = new StageZeroApp();
        var zones = Enumerable.Range(0, 60).Select(i => new { id = $"zone-{i}", name = $"example{i}.com" }).ToArray();
        app.Http.OnGet("https://api.cloudflare.com/client/v4/zones?page=1&", CloudflareFakes.Page(1, 2, zones[..50]));
        app.Http.OnGet("https://api.cloudflare.com/client/v4/zones?page=2&", CloudflareFakes.Page(2, 2, zones[50..]));

        using var scope = app.Services.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<ICloudflareService>().GetZonesAsync(CloudflareFakes.Token);

        Assert.Equal(60, listed.Count);
        Assert.Equal("example59.com", listed[^1].Name);
    }
}
