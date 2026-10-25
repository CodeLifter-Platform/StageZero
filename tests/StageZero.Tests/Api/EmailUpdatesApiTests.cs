using System.Net;
using System.Text.Json;
using Lifted.BlazorAuth.Basic.Services;
using StageZero.Api;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Api;

/// <summary>
/// Settings → Email from CodeLifter: the newsletter and StageZero updates, each its own
/// double opt-in request to codelifter.net, always for the signed-in operator's address.
/// </summary>
[Collection(AppCollection.Name)]
public class EmailUpdatesApiTests
{
    private const string SubscriptionsUrl = "https://codelifter.net/api/subscriptions";
    private const string Url = "/api/settings/email-updates";

    [Fact]
    public async Task Offers_the_two_options_for_the_signed_in_address()
    {
        await using var app = new StageZeroApp();
        using var client = await app.SignedInClientAsync("Owner@Example.com");

        var view = await (await client.GetAsync(Url)).ReadAsync<SettingsEndpoints.EmailUpdatesView>();

        Assert.True(view.Enabled);
        Assert.Equal("owner@example.com", view.Email, ignoreCase: true);
        Assert.Equal(["newsletter", "app-updates"], view.Choices.Select(c => c.Key));
    }

    [Theory]
    [InlineData("newsletter", true, false)]
    [InlineData("app-updates", false, true)]
    public async Task Each_option_asks_for_only_itself(string key, bool newsletter, bool appUpdates)
    {
        await using var app = new StageZeroApp();
        app.Http.On(HttpMethod.Post, SubscriptionsUrl, _ => FakeHttp.Text(
            """{"ok":true,"status":"pending","message":"Check your inbox to confirm.","confirming":"it"}"""));
        using var client = await app.SignedInClientAsync();

        var result = await (await client.PostJsonAsync(Url, new { key })).ReadAsync<SignupOptInResult>();

        Assert.True(result.Sent);
        var sent = Assert.Single(app.Http.Requests, r => r.Url.ToString() == SubscriptionsUrl);
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal("owner@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal(newsletter, body.RootElement.GetProperty("newsletter").GetBoolean());
        Assert.Equal(appUpdates, body.RootElement.GetProperty("appUpdates").GetBoolean());
    }

    [Fact]
    public async Task An_unknown_option_is_refused_without_a_request()
    {
        await using var app = new StageZeroApp();
        using var client = await app.SignedInClientAsync();

        var response = await client.PostJsonAsync(Url, new { key = "everything" });

        Assert.Equal("Pick the newsletter or StageZero updates.", await response.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.DoesNotContain(app.Http.Requests, r => r.Url.ToString() == SubscriptionsUrl);
    }

    [Fact]
    public async Task Needs_a_signed_in_operator()
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
    }
}
