using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StageZero.Api;
using StageZero.Models;
using StageZero.Services.Access;
using StageZero.Services.Tunnel;
using StageZero.Tests.Dns;
using StageZero.Tests.Fakes;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Api;

/// <summary>
/// Tunnel setup and routes over HTTP: the real store, sync and provisioning, with
/// Cloudflare's tunnel and Access APIs replaced by in-memory accounts.
/// </summary>
[Collection(AppCollection.Name)]
public class TunnelApiTests
{
    private const string AccountId = "account-1";

    private readonly FakeCloudflareTunnelService _tunnels = new();
    private readonly FakeCloudflareAccessService _access = new();

    private StageZeroApp CreateApp()
    {
        var app = new StageZeroApp
        {
            ConfigureServices = services =>
            {
                services.AddScoped<ICloudflareTunnelService>(_ => _tunnels);
                services.AddScoped<ICloudflareAccessService>(_ => _access);
            }
        };
        app.Http.OnGet("https://api.cloudflare.com/client/v4/zones", CloudflareFakes.List(new { id = "zone-1", name = "example.com" }));
        return app;
    }

    private static object Setup(string tunnelName = "stagezero") =>
        new { apiToken = CloudflareFakes.Token, accountId = AccountId, zoneId = "zone-1", tunnelName };

    private static object Route(string mode, string domain = "App.Example.com", bool createToken = false) => new
    {
        domainName = domain,
        forwardScheme = "http",
        forwardHost = "localhost",
        forwardPort = 8080,
        isEnabled = true,
        notes = (string?)null,
        access = new
        {
            mode,
            allowedEmails = mode is "identity" or "both" ? "owner@example.com" : null,
            sessionDuration = "24h",
            createServiceToken = createToken,
            serviceTokenName = createToken ? "app-token" : null,
        }
    };

    [Fact]
    public async Task Creating_a_tunnel_saves_it_without_echoing_the_api_token()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var created = await client.PostJsonAsync("/api/tunnel/create", Setup());
        var config = await client.GetAsync("/api/tunnel/config");

        var result = await created.ReadAsync<TunnelEndpoints.SetupView>();
        Assert.Equal(FakeCloudflareTunnelService.ConnectorToken, result.ConnectorToken);
        Assert.Equal("example.com", result.Config.ZoneName); // from Cloudflare, not the caller
        Assert.DoesNotContain(CloudflareFakes.Token, await created.Content.ReadAsStringAsync());
        Assert.DoesNotContain(CloudflareFakes.Token, await config.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Adopting_a_local_tunnel_switches_it_to_remote_config_as_cloudflare_reports_it()
    {
        await using var app = CreateApp();
        _tunnels.Tunnels.Add(new TunnelInfo { Id = "t-local", Name = "old", ConfigSrc = "local" });
        _tunnels.Tunnels.Add(new TunnelInfo { Id = "t-remote", Name = "new", ConfigSrc = "cloudflare" });
        using var client = await app.SignedInClientAsync();

        var local = await (await client.PostJsonAsync("/api/tunnel/adopt",
            new { apiToken = CloudflareFakes.Token, accountId = AccountId, zoneId = "zone-1", tunnelId = "t-local" }))
            .ReadAsync<TunnelEndpoints.SetupView>();
        var remote = await (await client.PostJsonAsync("/api/tunnel/adopt",
            new { apiToken = CloudflareFakes.Token, accountId = AccountId, zoneId = "zone-1", tunnelId = "t-remote" }))
            .ReadAsync<TunnelEndpoints.SetupView>();

        Assert.True(local.WasLocallyManaged);
        Assert.False(remote.WasLocallyManaged);
        Assert.Equal(["t-local"], _tunnels.Adopted);
    }

    [Fact]
    public async Task A_zone_the_token_cannot_see_is_refused()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var response = await client.PostJsonAsync("/api/tunnel/create",
            new { apiToken = CloudflareFakes.Token, accountId = AccountId, zoneId = "someone-elses-zone", tunnelName = "x" });

        Assert.Equal("That zone isn't visible to this token.", await response.ProblemTitleAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task A_route_before_setup_is_kept_and_the_reason_is_given()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var response = await client.PostJsonAsync("/api/tunnel/routes", Route("identity"));

        Assert.StartsWith("Could not save the route", await response.ProblemTitleAsync(HttpStatusCode.BadGateway));
        await using var db = await app.CreateDbContextAsync();
        Assert.Equal("app.example.com", (await db.TunnelRoutes.SingleAsync()).DomainName);
    }

    [Fact]
    public async Task A_new_service_token_secret_is_returned_once_and_stored_nowhere()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();
        await client.PostJsonAsync("/api/tunnel/create", Setup());

        var saved = await (await client.PostJsonAsync("/api/tunnel/routes", Route("service_token", createToken: true)))
            .ReadAsync<TunnelEndpoints.SaveResponse>();
        var secret = saved.NewServiceToken?.ClientSecret;

        Assert.False(string.IsNullOrEmpty(secret));
        Assert.Equal(["app.example.com"], _tunnels.Cnames);
        var reread = await client.GetAsync($"/api/tunnel/routes/{saved.Route.Id}");
        var list = await client.GetAsync("/api/tunnel/routes");
        Assert.DoesNotContain(secret!, await reread.Content.ReadAsStringAsync());
        Assert.DoesNotContain(secret!, await list.Content.ReadAsStringAsync());

        await using var db = await app.CreateDbContextAsync();
        var dump = string.Join("\n", (await db.TunnelRoutes.ToListAsync()).Select(r => System.Text.Json.JsonSerializer.Serialize(r)))
                   + string.Join("\n", (await db.AccessServiceTokens.ToListAsync()).Select(t => System.Text.Json.JsonSerializer.Serialize(t)));
        Assert.DoesNotContain(secret!, dump);
    }

    [Fact]
    public async Task Editing_a_route_keeps_the_access_objects_stagezero_provisioned()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();
        await client.PostJsonAsync("/api/tunnel/create", Setup());
        var created = await (await client.PostJsonAsync("/api/tunnel/routes", Route("identity"))).ReadAsync<TunnelEndpoints.SaveResponse>();
        string? applicationId;
        await using (var db = await app.CreateDbContextAsync())
        {
            applicationId = (await db.TunnelRoutes.SingleAsync()).Access.ApplicationId;
        }

        await client.PutJsonAsync($"/api/tunnel/routes/{created.Route.Id}", Route("identity"));

        Assert.NotNull(applicationId);
        Assert.Single(_access.Applications);
        await using var after = await app.CreateDbContextAsync();
        Assert.Equal(applicationId, (await after.TunnelRoutes.SingleAsync()).Access.ApplicationId);
    }

    [Fact]
    public async Task A_second_route_for_the_same_hostname_is_a_conflict()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();
        await client.PostJsonAsync("/api/tunnel/create", Setup());
        await client.PostJsonAsync("/api/tunnel/routes", Route("identity"));

        var duplicate = await client.PostJsonAsync("/api/tunnel/routes", Route("none", domain: "app.example.com "));

        Assert.Equal("A route for 'app.example.com' already exists.", await duplicate.ProblemTitleAsync(HttpStatusCode.Conflict));
    }

    [Theory]
    [InlineData("ftp", 8080, "Scheme must be http or https.")]
    [InlineData("http", 0, "Forward port must be between 1 and 65535.")]
    public async Task An_unusable_origin_is_refused_before_anything_is_saved(string scheme, int port, string message)
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var response = await client.PostJsonAsync("/api/tunnel/routes", new
        {
            domainName = "app.example.com", forwardScheme = scheme, forwardHost = "localhost", forwardPort = port,
            isEnabled = true, access = new { mode = "none" }
        });

        Assert.Equal(message, await response.ProblemTitleAsync(HttpStatusCode.BadRequest));
        await using var db = await app.CreateDbContextAsync();
        Assert.False(await db.TunnelRoutes.AnyAsync());
    }

    [Fact]
    public async Task New_routes_default_to_identity_for_the_owner()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync("Owner@Example.com");

        var defaults = await (await client.GetAsync("/api/tunnel/routes/defaults")).ReadAsync<TunnelEndpoints.AccessView>();

        Assert.Equal(AccessMode.Identity, defaults.Mode);
        Assert.Equal("owner@example.com", defaults.AllowedEmails);
    }

    [Fact]
    public async Task Disabling_drops_the_ingress_rule_and_keeps_the_cname_and_delete_removes_both()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();
        await client.PostJsonAsync("/api/tunnel/create", Setup());
        var created = await (await client.PostJsonAsync("/api/tunnel/routes", Route("none"))).ReadAsync<TunnelEndpoints.SaveResponse>();

        var toggled = await (await client.PostJsonAsync($"/api/tunnel/routes/{created.Route.Id}/toggle")).ReadAsync<TunnelEndpoints.SaveResponse>();

        Assert.False(toggled.Route.IsEnabled);
        Assert.Empty(_tunnels.LastIngress);
        Assert.Equal(["app.example.com"], _tunnels.Cnames);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/tunnel/routes/{created.Route.Id}")).StatusCode);
        Assert.Empty(_tunnels.Cnames);
    }
}
