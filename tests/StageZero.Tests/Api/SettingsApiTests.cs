using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StageZero.Api;
using StageZero.Services.Access;
using StageZero.Services.Tunnel;
using StageZero.Tests.Dns;
using StageZero.Tests.Fakes;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Api;

/// <summary>
/// The Settings page's Cloudflare token over HTTP: verified with (scripted) Cloudflare,
/// saved encrypted, checked against every permission StageZero needs, and never returned.
/// </summary>
[Collection(AppCollection.Name)]
public class SettingsApiTests
{
    private const string Api = "https://api.cloudflare.com/client/v4";
    private const string Url = "/api/settings/cloudflare";

    private readonly FakeCloudflareTunnelService _tunnels = new();
    private readonly FakeCloudflareAccessService _access = new();

    private StageZeroApp CreateApp(string verify, params object[] accounts)
    {
        var app = new StageZeroApp
        {
            ConfigureServices = services =>
            {
                services.AddScoped<ICloudflareTunnelService>(_ => _tunnels);
                services.AddScoped<ICloudflareAccessService>(_ => _access);
            }
        };
        app.Http.OnGet($"{Api}/user/tokens/verify", verify);
        app.Http.OnGet($"{Api}/accounts?", CloudflareFakes.List(accounts));
        app.Http.OnGet($"{Api}/zones?", CloudflareFakes.List(new { id = CloudflareFakes.ZoneId, name = "example.com" }));
        app.Http.OnGet(CloudflareFakes.ZoneUrl, CloudflareFakes.List());
        return app;
    }

    private static string Verified(object? notBefore = null, object? expiresOn = null) => JsonSerializer.Serialize(new
    {
        success = true,
        result = new { id = "token-id-1", status = "active", not_before = notBefore, expires_on = expiresOn }
    });

    private static readonly object Account1 = new { id = "account-1", name = "Home" };

    [Fact]
    public async Task Saving_verifies_the_token_finds_its_account_and_never_returns_it()
    {
        await using var app = CreateApp(Verified(expiresOn: "2030-01-01T00:00:00Z"), Account1);
        using var client = await app.SignedInClientAsync();

        var saved = await client.PutJsonAsync(Url, new { apiToken = $" {CloudflareFakes.Token} " });
        var read = await client.GetAsync(Url);

        var body = await saved.ReadAsync<SettingsEndpoints.CredentialResponse>();
        Assert.True(body.Status.IsConfigured);
        Assert.Equal(("account-1", "Home", "token-id-1"), (body.Status.AccountId, body.Status.AccountName, body.Status.TokenId));
        Assert.Equal(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), body.Status.ExpiresOn);
        Assert.All(body.Checks!, c => Assert.True(c.Granted, $"{c.Permission.Item}: {c.Detail}"));
        Assert.Equal(
            ["Cloudflare Tunnel", "Access: Apps and Policies", "Access: Service Tokens", "DNS", "Zone"],
            body.Checks!.Select(c => c.Permission.Item));
        Assert.DoesNotContain(CloudflareFakes.Token, await saved.Content.ReadAsStringAsync());
        Assert.DoesNotContain(CloudflareFakes.Token, await read.Content.ReadAsStringAsync());
        Assert.All(app.Http.Requests, r => Assert.Equal($"Bearer {CloudflareFakes.Token}", r.Authorization));
    }

    [Fact]
    public async Task The_permissions_table_is_served_before_any_token_is_saved()
    {
        await using var app = CreateApp(Verified());
        using var client = await app.SignedInClientAsync();

        var body = await (await client.GetAsync(Url)).ReadAsync<SettingsEndpoints.CredentialResponse>();

        Assert.False(body.Status.IsConfigured);
        Assert.Null(body.Checks);
        Assert.Contains(body.Required, p => (p.Scope, p.Item, p.Level) == ("Account", "Cloudflare Tunnel", "Edit"));
        Assert.Equal(5, body.Required.Count);
    }

    [Fact]
    public async Task A_token_that_is_not_valid_yet_is_refused_with_how_to_fix_it()
    {
        await using var app = CreateApp(Verified(notBefore: "2099-12-31T08:00:00Z"), Account1);
        using var client = await app.SignedInClientAsync();

        var response = await client.PutJsonAsync(Url, new { apiToken = CloudflareFakes.Token });

        var title = await response.ProblemTitleAsync(HttpStatusCode.BadRequest);
        Assert.Contains("can't be used until 2099-12-31 08:00 UTC", title);
        await using var db = await app.CreateDbContextAsync();
        Assert.False(await db.CloudflareCredentials.AnyAsync());
    }

    [Fact]
    public async Task A_token_cloudflare_rejects_is_not_saved()
    {
        await using var app = CreateApp("""{"success":false,"errors":[{"code":1000,"message":"Invalid API Token"}]}""");
        using var client = await app.SignedInClientAsync();

        var response = await client.PutJsonAsync(Url, new { apiToken = "nope" });

        Assert.Contains("Invalid API Token", await response.ProblemTitleAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task A_token_seeing_several_accounts_needs_the_account_id()
    {
        await using var app = CreateApp(Verified(), Account1, new { id = "account-2", name = "Work" });
        using var client = await app.SignedInClientAsync();

        var ambiguous = await client.PutJsonAsync(Url, new { apiToken = CloudflareFakes.Token });
        var chosen = await client.PutJsonAsync(Url, new { apiToken = CloudflareFakes.Token, accountId = "account-2" });

        Assert.Contains("several accounts", await ambiguous.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Work", (await chosen.ReadAsync<SettingsEndpoints.CredentialResponse>()).Status.AccountName);
    }

    [Fact]
    public async Task A_zone_only_token_is_saved_and_the_account_permissions_are_reported_missing()
    {
        await using var app = CreateApp(Verified());
        using var client = await app.SignedInClientAsync();

        var body = await (await client.PutJsonAsync(Url, new { apiToken = CloudflareFakes.Token }))
            .ReadAsync<SettingsEndpoints.CredentialResponse>();

        Assert.Null(body.Status.AccountId);
        Assert.Equal(
            [("Account", false), ("Account", false), ("Account", false), ("Zone", true), ("Zone", true)],
            body.Checks!.Select(c => (c.Permission.Scope, c.Granted)));
    }

    [Fact]
    public async Task Replacing_the_token_with_another_accounts_is_refused_while_a_tunnel_is_saved()
    {
        await using var app = CreateApp(Verified(), Account1);
        using var client = await app.SignedInClientAsync();
        await client.PutJsonAsync(Url, new { apiToken = CloudflareFakes.Token });
        await client.PostJsonAsync("/api/tunnel/create", new { zoneId = CloudflareFakes.ZoneId, tunnelName = "stagezero" });
        app.Http.OnGet($"{Api}/accounts?", CloudflareFakes.List(new { id = "account-2", name = "Work" }));

        var response = await client.PutJsonAsync(Url, new { apiToken = "other-token" });

        Assert.Contains("disconnect the tunnel", await response.ProblemTitleAsync(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Check_reverifies_and_remove_forgets_the_token()
    {
        await using var app = CreateApp(Verified(), Account1);
        using var client = await app.SignedInClientAsync();
        await client.PutJsonAsync(Url, new { apiToken = CloudflareFakes.Token });

        var checkedAgain = await (await client.PostJsonAsync($"{Url}/check")).ReadAsync<SettingsEndpoints.CredentialResponse>();
        var removed = await client.DeleteAsync(Url);
        var afterRemove = await (await client.GetAsync(Url)).ReadAsync<SettingsEndpoints.CredentialResponse>();
        var checkWithout = await client.PostJsonAsync($"{Url}/check");

        Assert.NotNull(checkedAgain.Status.VerifiedAt);
        Assert.Equal(5, checkedAgain.Checks!.Count);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.False(afterRemove.Status.IsConfigured);
        Assert.Contains("No Cloudflare API token is saved", await checkWithout.ProblemTitleAsync(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Settings_need_a_signed_in_operator()
    {
        await using var app = CreateApp(Verified());
        using var client = app.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
