using System.Net;
using StageZero.Models;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Auth;

/// <summary>
/// A Cloudflare Tunnel exists to hide the home IP. The app must not hand it — or its
/// navigation — to someone who hasn't signed in. (The signed-in side lives in a circuit,
/// not in a cold response, so it is checked by hand: see LivingSpec's platform matrix.)
/// </summary>
[Collection(AppCollection.Name)]
public class AnonymousChromeTests
{
    private const string HomeIp = "203.0.113.99";

    [Theory]
    [InlineData("/login")]
    [InlineData("/forgot-password")]
    [InlineData("/reset-password")]
    public async Task A_sign_in_page_shows_no_ip_and_no_navigation(string path)
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateNonRedirectingClient();
        await TestData.AddUserAsync(app, "owner@example.com", "correct horse battery");
        await RecordIpAsync(app);

        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(HomeIp, html);
        Assert.DoesNotContain("href=\"/dns-config\"", html);
        Assert.DoesNotContain("href=\"/tunnel-settings\"", html);
    }

    [Fact]
    public async Task First_run_setup_shows_no_ip_and_no_navigation()
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateNonRedirectingClient();
        await RecordIpAsync(app);

        var response = await client.GetAsync("/setup");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(HomeIp, html);
        Assert.DoesNotContain("href=\"/dns-config\"", html);
    }

    [Fact]
    public async Task A_protected_page_requested_cold_shows_no_ip()
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateNonRedirectingClient();
        await TestData.AddUserAsync(app, "owner@example.com", "correct horse battery");
        await RecordIpAsync(app);

        var response = await client.GetAsync("/tunnel-routes");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(HomeIp, html);
    }

    private static async Task RecordIpAsync(StageZeroApp app)
    {
        await using var db = await app.CreateDbContextAsync();
        db.IpChecks.Add(new IpCheck { IpAddress = HomeIp, CheckedAt = DateTime.UtcNow, IsChanged = true });
        await db.SaveChangesAsync();
    }
}
