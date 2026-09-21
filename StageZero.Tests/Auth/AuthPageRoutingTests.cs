using System.Net;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Auth;

/// <summary>
/// The auth pages live in the Lifted.BlazorAuth.Basic assembly. They have to answer a cold
/// request — a refresh, a bookmark, a link in an email — not only in-app navigation.
/// </summary>
[Collection(AppCollection.Name)]
public class AuthPageRoutingTests
{
    [Theory]
    [InlineData("/setup")]
    [InlineData("/forgot-password")]
    [InlineData("/reset-password")]
    public async Task Auth_page_answers_a_cold_request(string path)
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateNonRedirectingClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_sends_a_fresh_install_to_setup()
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateNonRedirectingClient();

        var response = await client.GetAsync("/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/setup", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Login_answers_a_cold_request_once_an_admin_exists()
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateNonRedirectingClient();
        await TestData.AddUserAsync(app, "owner@example.com", "correct horse battery");

        var response = await client.GetAsync("/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Forgot Password?", await response.Content.ReadAsStringAsync());
    }
}
