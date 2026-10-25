using System.Net;
using Microsoft.EntityFrameworkCore;
using StageZero.Api;
using StageZero.Services.Dns;
using StageZero.Tests.Dns;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Api;

/// <summary>
/// The DNS Configuration screen over HTTP, against the real store and a scripted Cloudflare.
/// Every provider uses the token saved on the Settings page; none is taken or returned here.
/// </summary>
[Collection(AppCollection.Name)]
public class DnsApiTests
{
    [Fact]
    public async Task A_provider_is_added_for_a_zone_without_a_token_of_its_own()
    {
        await using var app = new StageZeroApp();
        await CloudflareFakes.SaveTokenAsync(app);
        using var client = await app.SignedInClientAsync();

        var added = await client.PostJsonAsync("/api/dns/providers",
            new { name = "Home", zoneId = CloudflareFakes.ZoneId, isActive = false });
        var list = await client.GetAsync("/api/dns/providers");

        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.DoesNotContain(CloudflareFakes.Token, await list.Content.ReadAsStringAsync());
        var provider = Assert.Single(await list.ReadAsync<List<DnsEndpoints.ProviderView>>());
        Assert.False(provider.IsActive); // the dialog's switch used to be ignored
    }

    [Fact]
    public async Task Adding_a_provider_before_a_token_is_saved_points_to_settings()
    {
        await using var app = new StageZeroApp();
        using var client = await app.SignedInClientAsync();

        var response = await client.PostJsonAsync("/api/dns/providers", new { name = "Home", zoneId = CloudflareFakes.ZoneId });

        Assert.Contains("Settings page", await response.ProblemTitleAsync(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task A_cname_without_a_target_is_refused_with_a_reason()
    {
        await using var app = new StageZeroApp();
        var provider = await CloudflareFakes.AddProviderAsync(app);
        using var client = await app.SignedInClientAsync();

        var response = await client.PostJsonAsync($"/api/dns/providers/{provider.Id}/records",
            new { recordName = "www.example.com", recordType = "CNAME", autoUpdate = false });

        Assert.Equal("CNAME target is required.", await response.ProblemTitleAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task An_unsupported_record_type_is_refused()
    {
        await using var app = new StageZeroApp();
        var provider = await CloudflareFakes.AddProviderAsync(app);
        using var client = await app.SignedInClientAsync();

        var response = await client.PostJsonAsync($"/api/dns/providers/{provider.Id}/records",
            new { recordName = "example.com", recordType = "MX", autoUpdate = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Editing_a_record_to_aaaa_switches_its_auto_update_off()
    {
        await using var app = new StageZeroApp();
        var record = new Models.DnsRecord { RecordName = "home.example.com", RecordType = "A", AutoUpdate = true };
        await CloudflareFakes.AddProviderAsync(app, record);
        using var client = await app.SignedInClientAsync();
        var recordId = record.Id;

        var edited = await (await client.PutJsonAsync($"/api/dns/records/{recordId}",
            new { recordName = "home.example.com", recordType = "AAAA", autoUpdate = true }))
            .ReadAsync<DnsEndpoints.RecordView>();

        Assert.False(edited.AutoUpdate);
        Assert.False(edited.AutoUpdateSupported);
    }

    [Fact]
    public async Task Import_adds_the_tracked_types_once_and_skips_what_is_already_there()
    {
        await using var app = new StageZeroApp();
        var provider = await CloudflareFakes.AddProviderAsync(app,
            new Models.DnsRecord { RecordName = "home.example.com", RecordType = "A", AutoUpdate = true });
        app.Http.OnGet(CloudflareFakes.ZoneUrl, CloudflareFakes.List(
            CloudflareFakes.Record("r1", "home.example.com", "A", "203.0.113.1"),
            CloudflareFakes.Record("r2", "www.example.com", "CNAME", "home.example.com"),
            CloudflareFakes.Record("r3", "v6.example.com", "AAAA", "2001:db8::1"),
            CloudflareFakes.Record("r4", "example.com", "MX", "mail.example.com")));
        using var client = await app.SignedInClientAsync();

        var result = await (await client.PostJsonAsync($"/api/dns/providers/{provider.Id}/import")).ReadAsync<DnsImportResult>();

        Assert.Equal(new DnsImportResult(2, 1), result);
        await using var db = await app.CreateDbContextAsync();
        var records = await db.DnsRecords.OrderBy(r => r.RecordName).ToListAsync();
        Assert.Equal(["home.example.com", "v6.example.com", "www.example.com"], records.Select(r => r.RecordName));
        Assert.Equal("home.example.com", records.Single(r => r.RecordType == "CNAME").Content);
        Assert.All(records.Where(r => r.RecordType != "A"), r => Assert.False(r.AutoUpdate));
    }

    [Fact]
    public async Task A_cloudflare_failure_is_reported_in_words_not_as_a_crash()
    {
        await using var app = new StageZeroApp();
        var provider = await CloudflareFakes.AddProviderAsync(app);
        app.Http.OnGet(CloudflareFakes.ZoneUrl, """{"success":false,"errors":[{"code":10000,"message":"Authentication error"}]}""",
            HttpStatusCode.Forbidden);
        using var client = await app.SignedInClientAsync();

        var response = await client.GetAsync($"/api/dns/providers/{provider.Id}/cloudflare-records");

        Assert.StartsWith("Failed to load DNS records", await response.ProblemTitleAsync(HttpStatusCode.BadGateway));
    }

    [Fact]
    public async Task Deleting_a_provider_that_is_gone_is_a_404()
    {
        await using var app = new StageZeroApp();
        using var client = await app.SignedInClientAsync();

        var response = await client.DeleteAsync("/api/dns/providers/999");

        Assert.Equal("That DNS provider no longer exists.", await response.ProblemTitleAsync(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Zones_are_listed_with_the_saved_token()
    {
        await using var app = new StageZeroApp();
        await CloudflareFakes.SaveTokenAsync(app);
        app.Http.OnGet("https://api.cloudflare.com/client/v4/zones", CloudflareFakes.List(new { id = "zone-1", name = "example.com" }));
        using var client = await app.SignedInClientAsync();

        var zones = await (await client.GetAsync("/api/dns/zones")).ReadAsync<List<CloudflareZone>>();

        Assert.Equal("example.com", Assert.Single(zones).Name);
        var request = Assert.Single(app.Http.Requests);
        Assert.Equal($"Bearer {CloudflareFakes.Token}", request.Authorization);
        Assert.DoesNotContain(CloudflareFakes.Token, request.Url.ToString());
    }
}
