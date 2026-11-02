using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StageZero.Api;
using StageZero.Services.Storage;
using StageZero.Tests.Dns;
using StageZero.Tests.Fakes;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Api;

/// <summary>
/// R2 buckets and their domains over HTTP: the real rules and the saved token, with Cloudflare's
/// R2 API replaced by an in-memory account and its zones answered by FakeHttp.
/// </summary>
[Collection(AppCollection.Name)]
public class StorageApiTests
{
    private const string AccountId = "account-1";
    private const string Url = "/api/storage/buckets";

    private readonly FakeCloudflareR2Service _r2 = new();

    private StageZeroApp CreateApp(bool withToken = true, string? accountId = AccountId)
    {
        var app = new StageZeroApp
        {
            ConfigureServices = services => services.AddScoped<ICloudflareR2Service>(_ => _r2)
        };
        app.Http.OnGet("https://api.cloudflare.com/client/v4/zones",
            CloudflareFakes.List(new { id = "zone-1", name = "example.com" }, new { id = "zone-2", name = "files.example.com" }));
        if (withToken)
        {
            CloudflareFakes.SaveTokenAsync(app, accountId).GetAwaiter().GetResult();
        }

        return app;
    }

    [Fact]
    public async Task Buckets_are_listed_created_and_deleted_with_the_saved_token_which_never_comes_back()
    {
        await using var app = CreateApp();
        _r2.Buckets.Add(new R2Bucket { Name = "existing", StorageClass = "Standard" });
        using var client = await app.SignedInClientAsync();

        var created = await client.PostJsonAsync(Url, new { name = " backups ", locationHint = "WNAM", storageClass = (string?)null });
        var listed = await client.GetAsync(Url);
        var deleted = await client.DeleteAsync($"{Url}/existing");

        var bucket = await created.ReadAsync<StorageEndpoints.BucketView>();
        Assert.Equal(("backups", "wnam"), (bucket.Name, bucket.Location)); // trimmed, and the hint normalised to Cloudflare's spelling
        var list = await listed.ReadAsync<StorageEndpoints.BucketsResponse>();
        Assert.Equal(["existing", "backups"], list.Buckets.Select(b => b.Name));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(["existing"], _r2.Deleted);
        Assert.DoesNotContain(CloudflareFakes.Token, await created.Content.ReadAsStringAsync());
        Assert.DoesNotContain(CloudflareFakes.Token, await listed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_bucket_name_cloudflare_would_refuse_is_refused_here_first_and_a_duplicate_is_a_conflict()
    {
        await using var app = CreateApp();
        _r2.Buckets.Add(new R2Bucket { Name = "taken" });
        using var client = await app.SignedInClientAsync();

        var upper = await client.PostJsonAsync(Url, new { name = "MyBucket" });
        var tooShort = await client.PostJsonAsync(Url, new { name = "ab" });
        var badHint = await client.PostJsonAsync(Url, new { name = "fine-name", locationHint = "moon" });
        var duplicate = await client.PostJsonAsync(Url, new { name = "taken" });

        Assert.Contains("lowercase", await upper.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Contains("3 to 63", await tooShort.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Contains("location hint", await badHint.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Contains("already exists", await duplicate.ProblemTitleAsync(HttpStatusCode.Conflict));
        Assert.Single(_r2.Buckets); // nothing reached Cloudflare
    }

    [Fact]
    public async Task Deleting_a_bucket_that_still_holds_objects_passes_cloudflares_refusal_through()
    {
        await using var app = CreateApp();
        _r2.Buckets.Add(new R2Bucket { Name = "full" });
        _r2.NotEmpty.Add("full");
        using var client = await app.SignedInClientAsync();

        var response = await client.DeleteAsync($"{Url}/full");

        Assert.Contains("not empty", await response.ProblemTitleAsync(HttpStatusCode.BadGateway));
        Assert.Single(_r2.Buckets);
    }

    [Fact]
    public async Task A_custom_domain_lands_in_the_zone_cloudflare_knows_and_never_one_the_token_cannot_see()
    {
        await using var app = CreateApp();
        _r2.Buckets.Add(new R2Bucket { Name = "media" });
        using var client = await app.SignedInClientAsync();

        var attached = await client.PostJsonAsync($"{Url}/media/domains/custom", new { domain = "CDN.Files.Example.com.", enabled = true, minTls = "1.2" });
        var outside = await client.PostJsonAsync($"{Url}/media/domains/custom", new { domain = "cdn.other.net" });
        var notAHost = await client.PostJsonAsync($"{Url}/media/domains/custom", new { domain = "just-a-word" });
        var domains = await (await client.GetAsync($"{Url}/media/domains")).ReadAsync<StorageEndpoints.DomainsView>();

        var view = await attached.ReadAsync<StorageEndpoints.CustomDomainView>();
        Assert.Equal(("cdn.files.example.com", "zone-2", "files.example.com", "1.2"), (view.Domain, view.ZoneId, view.ZoneName, view.MinTls)); // the most specific zone
        Assert.Contains("isn't in a zone this token can see", await outside.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Contains("full hostname", await notAHost.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Equal(["cdn.files.example.com"], domains.Custom.Select(d => d.Domain));
        Assert.Equal(["example.com", "files.example.com"], domains.Zones.Select(z => z.Name));
        Assert.False(domains.Managed.Enabled);
    }

    [Fact]
    public async Task Public_access_and_a_domains_settings_are_toggled_and_a_domain_is_removed()
    {
        await using var app = CreateApp();
        _r2.Buckets.Add(new R2Bucket { Name = "site" });
        using var client = await app.SignedInClientAsync();
        await client.PostJsonAsync($"{Url}/site/domains/custom", new { domain = "www.example.com" });

        var open = await (await client.PutJsonAsync($"{Url}/site/domains/managed", new { enabled = true })).ReadAsync<StorageEndpoints.ManagedView>();
        var paused = await (await client.PutJsonAsync($"{Url}/site/domains/custom/www.example.com", new { enabled = false, minTls = "1.3" })).ReadAsync<StorageEndpoints.CustomDomainView>();
        var removed = await client.DeleteAsync($"{Url}/site/domains/custom/www.example.com");
        var after = await (await client.GetAsync($"{Url}/site/domains")).ReadAsync<StorageEndpoints.DomainsView>();

        Assert.True(open.Enabled);
        Assert.Equal("pub-site.r2.dev", open.Domain);
        Assert.Equal((false, "1.3"), (paused.Enabled, paused.MinTls));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(after.Custom);
    }

    [Fact]
    public async Task Without_an_account_token_storage_says_what_to_fix_and_anonymous_callers_are_refused()
    {
        await using var app = CreateApp(accountId: null);
        using var client = await app.SignedInClientAsync();
        using var anonymous = app.CreateApiClient();

        var noAccount = await client.GetAsync(Url);
        var unauthenticated = await anonymous.GetAsync(Url);

        Assert.Contains("no account permissions", await noAccount.ProblemTitleAsync(HttpStatusCode.Conflict));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task Without_any_token_storage_points_at_settings()
    {
        await using var app = CreateApp(withToken: false);
        using var client = await app.SignedInClientAsync();

        var response = await client.GetAsync(Url);

        Assert.Contains("Settings page", await response.ProblemTitleAsync(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task The_options_endpoint_lists_cloudflares_fixed_choices()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var options = await (await client.GetAsync("/api/storage/options")).ReadAsync<StorageEndpoints.OptionsView>();

        Assert.Equal(["wnam", "enam", "weur", "eeur", "apac", "oc"], options.LocationHints);
        Assert.Equal(["Standard", "InfrequentAccess"], options.StorageClasses);
        Assert.Equal(["1.0", "1.1", "1.2", "1.3"], options.MinTlsVersions);
    }
}
