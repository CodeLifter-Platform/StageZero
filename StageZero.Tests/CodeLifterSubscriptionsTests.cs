using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Services.CodeLifter;

namespace StageZero.Tests;

public class CodeLifterSubscriptionsTests
{
    /// <summary>Records the one request the client sends and answers with a canned response.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
            _ => JsonResponse(HttpStatusCode.OK, """{"ok":true,"status":"pending","message":"Check your inbox to confirm.","confirming":"StageZero updates"}""");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Respond(request);
        }

        public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static CodeLifterSubscriptions Create(FakeHandler handler, string? url = "https://example.test/api/subscriptions") =>
        new(new CodeLifterSubscriptionsOptions { SubscriptionsUrl = url }, new FakeFactory(handler), NullLogger<CodeLifterSubscriptions>.Instance);

    [Fact]
    public void Offers_the_newsletter_and_app_updates_when_enabled()
    {
        var subs = Create(new FakeHandler());

        Assert.Equal(new[] { "newsletter", "app-updates" }, subs.Choices.Select(c => c.Key));
        Assert.All(subs.Choices, c => Assert.False(string.IsNullOrWhiteSpace(c.Description)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://example.test/x")]
    public void A_blank_or_unusable_url_turns_the_boxes_off(string url)
    {
        var subs = Create(new FakeHandler(), url);

        Assert.Empty(subs.Choices);
    }

    [Fact]
    public void Configuration_absent_means_the_default_endpoint_and_present_but_blank_means_off()
    {
        var absent = CodeLifterSubscriptionsOptions.FromConfiguration(new ConfigurationBuilder().Build());
        Assert.Equal(CodeLifterSubscriptionsOptions.DefaultSubscriptionsUrl, absent.SubscriptionsUrl);
        Assert.Equal("stagezero", absent.AppId);
        Assert.True(absent.Enabled);

        var blank = CodeLifterSubscriptionsOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CodeLifter:SubscriptionsUrl"] = "" })
            .Build());
        Assert.False(blank.Enabled);
    }

    [Fact]
    public async Task Posts_the_documented_payload_and_reports_what_to_confirm()
    {
        var handler = new FakeHandler();
        var subs = Create(handler);

        var result = await subs.SubmitAsync("Ada@Example.test", new[] { "newsletter", "app-updates" });

        Assert.True(result.Sent);
        Assert.Equal("Check Ada@Example.test to confirm StageZero updates.", result.Message);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://example.test/api/subscriptions", handler.Request.RequestUri!.ToString());
        Assert.Equal("application/json", handler.Request.Content!.Headers.ContentType!.MediaType);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var root = body.RootElement;
        Assert.Equal("Ada@Example.test", root.GetProperty("email").GetString());
        Assert.True(root.GetProperty("newsletter").GetBoolean());
        Assert.Equal("stagezero", root.GetProperty("app").GetString());
        Assert.True(root.GetProperty("appUpdates").GetBoolean());
        Assert.Equal("stagezero", root.GetProperty("source").GetString());
        var version = root.GetProperty("appVersion").GetString();
        Assert.False(string.IsNullOrEmpty(version));
        Assert.DoesNotContain("+", version); // commit suffix stripped
    }

    [Fact]
    public async Task Only_the_ticked_boxes_are_sent()
    {
        var handler = new FakeHandler();
        var subs = Create(handler);

        await subs.SubmitAsync("ada@example.test", new[] { "newsletter" });

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.True(body.RootElement.GetProperty("newsletter").GetBoolean());
        Assert.False(body.RootElement.GetProperty("appUpdates").GetBoolean());
        // The app still identifies itself so the site knows where the signup came from.
        Assert.Equal("stagezero", body.RootElement.GetProperty("app").GetString());
    }

    [Fact]
    public async Task Nothing_ticked_means_no_request()
    {
        var handler = new FakeHandler();
        var subs = Create(handler);

        var result = await subs.SubmitAsync("ada@example.test", Array.Empty<string>());

        Assert.False(result.Sent);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Already_subscribed_is_reported_without_claiming_a_send()
    {
        var handler = new FakeHandler
        {
            Respond = _ => FakeHandler.JsonResponse(HttpStatusCode.OK,
                """{"ok":true,"status":"nothing-new","message":"You're already subscribed. Nothing to confirm.","confirming":null}"""),
        };
        var subs = Create(handler);

        var result = await subs.SubmitAsync("ada@example.test", new[] { "app-updates" });

        Assert.False(result.Sent);
        Assert.Equal("You're already subscribed. Nothing to confirm.", result.Message);
    }

    [Fact]
    public async Task A_rejection_carries_the_servers_reason_and_never_throws()
    {
        var handler = new FakeHandler
        {
            Respond = _ => FakeHandler.JsonResponse(HttpStatusCode.BadRequest, """{"error":"Unknown app id: stagezero."}"""),
        };
        var subs = Create(handler);

        var result = await subs.SubmitAsync("ada@example.test", new[] { "app-updates" });

        Assert.False(result.Sent);
        Assert.Equal("Couldn't sign you up: Unknown app id: stagezero.", result.Message);
    }

    [Fact]
    public async Task A_rate_limit_or_outage_is_reported_and_never_throws()
    {
        var limited = new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) };
        var down = new FakeHandler { Respond = _ => throw new HttpRequestException("connection refused") };

        var limitedResult = await Create(limited).SubmitAsync("ada@example.test", new[] { "newsletter" });
        Assert.False(limitedResult.Sent);
        Assert.Contains("429", limitedResult.Message);

        var downResult = await Create(down).SubmitAsync("ada@example.test", new[] { "newsletter" });
        Assert.False(downResult.Sent);
        Assert.Contains("Couldn't reach codelifter.net", downResult.Message);
        Assert.Contains("Your account is fine", downResult.Message);
    }

    [Fact]
    public async Task A_non_json_success_body_still_counts_as_not_sent_rather_than_crashing()
    {
        var handler = new FakeHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") },
        };

        var result = await Create(handler).SubmitAsync("ada@example.test", new[] { "newsletter" });

        Assert.False(result.Sent);
    }
}
