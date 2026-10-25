using System.Net;
using Lifted.BlazorAuth.Basic.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StageZero.Api;
using StageZero.Models;
using StageZero.Tests.Fakes;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Api;

/// <summary>
/// The trust boundary: what an anonymous caller can reach, what a cookie proves, and when a
/// session ends. A Cloudflare Tunnel exists to hide the home IP, so nothing open may carry it.
/// </summary>
[Collection(AppCollection.Name)]
public class SessionTests
{
    private const string HomeIp = "203.0.113.99";

    public static TheoryData<string> OperatorRoutes =>
    [
        "/api/ip/current",
        "/api/ip/history",
        "/api/ip/events",
        "/api/dns/providers",
        "/api/tunnel/config",
        "/api/tunnel/routes",
        "/api/tunnel/routes/defaults",
        "/api/tunnel/access/service-tokens",
    ];

    [Theory]
    [MemberData(nameof(OperatorRoutes))]
    public async Task An_operator_route_refuses_a_caller_who_has_not_signed_in(string path)
    {
        await using var app = new StageZeroApp();
        await TestData.AddUserAsync(app, "owner@example.com", TestApi.Password);
        await RecordIpAsync(app);
        using var client = app.CreateApiClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(HomeIp, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_forged_cookie_is_refused()
    {
        await using var app = new StageZeroApp();
        await TestData.AddUserAsync(app, "owner@example.com", TestApi.Password);
        using var client = app.CreateApiClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{Session.CookieName}=CfDJ8not-a-real-ticket");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ip/current")).StatusCode);
    }

    [Fact]
    public async Task The_open_session_route_says_a_fresh_install_needs_setup_and_carries_no_ip()
    {
        await using var app = new StageZeroApp();
        await RecordIpAsync(app);
        using var client = app.CreateApiClient();

        var response = await client.GetAsync("/api/auth/session");
        var session = await response.ReadAsync<AuthEndpoints.SessionResponse>();

        Assert.True(session.SetupRequired);
        Assert.Null(session.User);
        Assert.DoesNotContain(HomeIp, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_sign_in_survives_a_reload()
    {
        // The Blazor app kept the user per circuit, so a reload signed them out.
        await using var app = new StageZeroApp();
        await RecordIpAsync(app);
        using var client = await app.SignedInClientAsync();

        var first = await (await client.GetAsync("/api/ip/current")).ReadAsync<IpEndpoints.CurrentIp>();
        var session = await (await client.GetAsync("/api/auth/session")).ReadAsync<AuthEndpoints.SessionResponse>();

        Assert.Equal(HomeIp, first.IpAddress);
        Assert.Equal("owner@example.com", session.User?.Email);
    }

    [Fact]
    public async Task The_session_cookie_is_http_only_and_same_site_strict()
    {
        await using var app = new StageZeroApp();
        await TestData.AddUserAsync(app, "owner@example.com", TestApi.Password);
        using var client = app.CreateApiClient();

        var response = await client.PostJsonAsync("/api/auth/login", new { email = "owner@example.com", password = TestApi.Password });

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(Session.CookieName, StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_wrong_password_says_so_and_issues_no_session()
    {
        await using var app = new StageZeroApp();
        await TestData.AddUserAsync(app, "owner@example.com", TestApi.Password);
        using var client = app.CreateApiClient();

        var response = await client.PostJsonAsync("/api/auth/login", new { email = "owner@example.com", password = "nope" });

        Assert.Equal("Invalid email or password", await response.ProblemTitleAsync(HttpStatusCode.Unauthorized));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ip/current")).StatusCode);
    }

    [Fact]
    public async Task A_state_changing_call_without_the_request_header_is_refused_before_it_runs()
    {
        await using var app = new StageZeroApp();
        await TestData.AddUserAsync(app, "owner@example.com", TestApi.Password);
        using var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostJsonAsync("/api/auth/login", new { email = "owner@example.com", password = TestApi.Password });

        Assert.Contains(ApiPipeline.RequestHeader, await login.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.False(login.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Changing_the_password_ends_every_other_session_and_keeps_this_one()
    {
        await using var app = new StageZeroApp();
        using var laptop = await app.SignedInClientAsync();
        using var phone = app.CreateApiClient();
        await phone.PostJsonAsync("/api/auth/login", new { email = "owner@example.com", password = TestApi.Password });

        var change = await laptop.PostJsonAsync("/api/auth/account/change-password",
            new { currentPassword = TestApi.Password, newPassword = "a brand new password" });

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/dns/providers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/dns/providers")).StatusCode);
    }

    [Fact]
    public async Task Deactivating_the_account_ends_its_session()
    {
        await using var app = new StageZeroApp();
        using var client = await app.SignedInClientAsync();

        await using (var db = await app.CreateDbContextAsync())
        {
            await db.Users.ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dns/providers")).StatusCode);
    }

    [Fact]
    public async Task A_lockout_from_elsewhere_ends_the_session_too()
    {
        // Five wrong passwords from another address lock the account; a session already
        // open must not outlive that.
        await using var app = new StageZeroApp();
        using var client = await app.SignedInClientAsync();

        await using (var db = await app.CreateDbContextAsync())
        {
            await db.Users.ExecuteUpdateAsync(u => u.SetProperty(x => x.LockoutEndsAt, DateTime.UtcNow.AddMinutes(15)));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dns/providers")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        await using var app = new StageZeroApp();
        using var client = await app.SignedInClientAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostJsonAsync("/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dns/providers")).StatusCode);
    }

    [Fact]
    public async Task A_user_owing_account_setup_can_only_finish_it()
    {
        await using var app = new StageZeroApp();
        var user = await TestData.AddUserAsync(app, "owner@example.com", TestApi.Password);
        await using (var db = await app.CreateDbContextAsync())
        {
            // What `reset-password` on the server leaves: a one-time password to replace.
            await db.Users.ExecuteUpdateAsync(u => u.SetProperty(x => x.RequiresPasswordChange, true));
        }

        using var client = app.CreateApiClient();
        var login = await client.PostJsonAsync("/api/auth/login", new { email = user.Email, password = TestApi.Password });
        var signedIn = await login.ReadAsync<AuthEndpoints.SessionUser>();

        Assert.True(signedIn.AccountSetupRequired);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dns/providers")).StatusCode);

        var change = await client.PostJsonAsync("/api/auth/account/change-password",
            new { currentPassword = TestApi.Password, newPassword = "a brand new password" });

        Assert.False((await change.ReadAsync<AuthEndpoints.SessionUser>()).AccountSetupRequired);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dns/providers")).StatusCode);
    }

    [Fact]
    public async Task A_new_password_shorter_than_eight_characters_is_refused()
    {
        await using var app = new StageZeroApp();
        using var client = await app.SignedInClientAsync();

        var change = await client.PostJsonAsync("/api/auth/account/change-password",
            new { currentPassword = TestApi.Password, newPassword = "short" });

        Assert.Contains("at least 8", await change.ProblemTitleAsync(HttpStatusCode.BadRequest));
    }

    // ── first-run setup ─────────────────────────────────────────

    [Fact]
    public async Task Setup_creates_the_admin_once_and_then_closes()
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateApiClient();

        var first = await client.PostJsonAsync("/api/auth/setup", new { email = " owner@example.com ", password = TestApi.Password });
        var second = await client.PostJsonAsync("/api/auth/setup", new { email = "intruder@example.com", password = TestApi.Password });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Setup has already been completed.", await second.ProblemTitleAsync(HttpStatusCode.Conflict));
        await using var db = await app.CreateDbContextAsync();
        var admin = await db.Users.SingleAsync();
        Assert.Equal("owner@example.com", admin.Email);
        Assert.True(admin.EmailVerified);
    }

    private const string RegisterUrl = "https://codelifter.net/api/app-users/register";

    [Fact]
    public async Task Setup_registers_the_admin_with_codelifter_and_asks_for_no_subscription()
    {
        await using var app = new StageZeroApp();
        app.Http.On(HttpMethod.Post, RegisterUrl, _ => FakeHttp.Text("""{"ok":true}"""));
        using var client = app.CreateApiClient();

        var offered = await (await client.GetAsync("/api/auth/session")).ReadAsync<AuthEndpoints.SessionResponse>();
        var response = await client.PostJsonAsync("/api/auth/setup", new { email = "owner@example.com", password = TestApi.Password });

        Assert.True(offered.CodeLifterRegistration);
        Assert.True((await response.ReadAsync<AuthEndpoints.SetupResponse>()).RegisteredWithCodeLifter);
        var sent = Assert.Single(app.Http.Requests);
        Assert.Equal(RegisterUrl, sent.Url.ToString());
        using var body = System.Text.Json.JsonDocument.Parse(sent.Body!);
        Assert.Equal("owner@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("stagezero", body.RootElement.GetProperty("app").GetString());
        Assert.False(body.RootElement.TryGetProperty("newsletter", out _));
    }

    [Fact]
    public async Task Setup_sends_nothing_to_codelifter_when_the_box_is_unticked()
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateApiClient();

        var response = await client.PostJsonAsync("/api/auth/setup",
            new { email = "owner@example.com", password = TestApi.Password, registerWithCodeLifter = false });

        Assert.False((await response.ReadAsync<AuthEndpoints.SetupResponse>()).RegisteredWithCodeLifter);
        Assert.Empty(app.Http.Requests);
    }

    [Fact]
    public async Task Setup_succeeds_when_codelifter_is_unreachable()
    {
        await using var app = new StageZeroApp(); // no fake route: every request to the site fails
        using var client = app.CreateApiClient();

        var response = await client.PostJsonAsync("/api/auth/setup", new { email = "owner@example.com", password = TestApi.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.ReadAsync<AuthEndpoints.SetupResponse>()).RegisteredWithCodeLifter);
    }

    [Theory]
    [InlineData("not-an-email", TestApi.Password, "valid email")]
    [InlineData("owner@example.com", "short", "at least 8")]
    public async Task Setup_refuses_an_unusable_form(string email, string password, string message)
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateApiClient();

        var response = await client.PostJsonAsync("/api/auth/setup", new { email, password });

        Assert.Contains(message, await response.ProblemTitleAsync(HttpStatusCode.BadRequest));
        await using var db = await app.CreateDbContextAsync();
        Assert.False(await db.Users.AnyAsync());
    }

    // ── password reset ──────────────────────────────────────────

    [Fact]
    public async Task A_reset_code_from_the_log_resets_the_password_and_leaves_email_verification_owing()
    {
        var codes = new FakeCodeSink();
        await using var app = new StageZeroApp
        {
            ConfigureServices = s => s.AddScoped<IEmailService>(_ => codes)
        };
        await TestData.AddUserAsync(app, "owner@example.com", TestApi.Password);
        using var client = app.CreateApiClient();

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostJsonAsync("/api/auth/forgot-password", new { email = "owner@example.com" })).StatusCode);
        var reset = await client.PostJsonAsync("/api/auth/reset-password",
            new { email = "owner@example.com", code = codes.Sent.Single().Code, newPassword = "a brand new password" });
        var login = await client.PostJsonAsync("/api/auth/login",
            new { email = "owner@example.com", password = "a brand new password" });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.True((await login.ReadAsync<AuthEndpoints.SessionUser>()).RequiresEmailVerification);
    }

    [Fact]
    public async Task Forgot_password_answers_alike_for_an_unknown_address()
    {
        var codes = new FakeCodeSink();
        await using var app = new StageZeroApp { ConfigureServices = s => s.AddScoped<IEmailService>(_ => codes) };
        await TestData.AddUserAsync(app, "owner@example.com", TestApi.Password);
        using var client = app.CreateApiClient();

        var response = await client.PostJsonAsync("/api/auth/forgot-password", new { email = "nobody@example.com" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(codes.Sent);
    }

    [Fact]
    public async Task Reset_requests_past_the_limit_are_throttled_with_a_retry_time()
    {
        await using var app = new StageZeroApp { ConfigureServices = s => s.AddScoped<IEmailService>(_ => new FakeCodeSink()) };
        using var client = app.CreateApiClient();

        for (var i = 0; i < AuthThrottle.MaxFailures; i++)
        {
            await client.PostJsonAsync("/api/auth/forgot-password", new { email = "owner@example.com" });
        }

        var refused = await client.PostJsonAsync("/api/auth/forgot-password", new { email = "owner@example.com" });

        Assert.StartsWith("Too many attempts", await refused.ProblemTitleAsync(HttpStatusCode.TooManyRequests));
    }

    private static async Task RecordIpAsync(StageZeroApp app)
    {
        await using var db = await app.CreateDbContextAsync();
        db.IpChecks.Add(new IpCheck { IpAddress = HomeIp, CheckedAt = DateTime.UtcNow, IsChanged = true });
        await db.SaveChangesAsync();
    }
}
