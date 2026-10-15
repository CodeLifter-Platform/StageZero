using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StageZero.DataAdapters.IpChecks;
using StageZero.Models;
using StageZero.Services.IpMonitoring;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Api;

/// <summary>
/// How the host answers what isn't a normal API call: the web app's deep links, unknown
/// API routes, an unhandled failure, and the push stream of IP changes.
/// </summary>
[Collection(AppCollection.Name)]
public class HostingTests
{
    [Fact]
    public async Task A_deep_link_or_reload_gets_the_web_app_uncached()
    {
        var webRoot = Directory.CreateTempSubdirectory("stagezero-web-").FullName;
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><div id=root></div>");
        try
        {
            // Production: in Development, static web assets also layer in the project's own
            // wwwroot (whatever the last web build left there), which would mask this one.
            await using var app = new StageZeroApp(environment: "Production") { WebRoot = webRoot };
            using var client = app.CreateApiClient();

            var response = await client.GetAsync("/tunnel-routes/edit/3");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("id=root", await response.Content.ReadAsStringAsync());
            Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Without_a_built_web_app_the_server_says_how_to_build_it()
    {
        var webRoot = Directory.CreateTempSubdirectory("stagezero-web-").FullName;
        try
        {
            // Production: in Development, static web assets also layer in the project's own
            // wwwroot (whatever the last web build left there), which would mask this one.
            await using var app = new StageZeroApp(environment: "Production") { WebRoot = webRoot };
            using var client = app.CreateApiClient();

            var response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("npm run build", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task An_unknown_api_route_is_a_404_problem_not_the_web_app()
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateApiClient();

        var response = await client.GetAsync("/api/no-such-thing");

        Assert.Equal("No such API endpoint.", await response.ProblemTitleAsync(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task An_unhandled_failure_returns_a_reference_and_nothing_from_the_exception()
    {
        await using var app = new StageZeroApp(environment: "Production")
        {
            ConfigureServices = s => s.AddScoped<IIpCheckReader, ExplodingIpCheckReader>()
        };
        using var client = await app.SignedInClientAsync();

        var response = await client.GetAsync("/api/ip/current");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("traceId", body);
        Assert.DoesNotContain("secret internal detail", body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
    }

    [Fact]
    public async Task An_ip_change_is_pushed_to_a_signed_in_listener()
    {
        await using var app = new StageZeroApp();
        using var client = await app.SignedInClientAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // The stream opens with the IP as it stands (none yet), then carries each change.
        using var response = await client.GetAsync("/api/ip/events", HttpCompletionOption.ResponseHeadersRead, cancel.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancel.Token));

        // The subscription starts when the stream does; publish until the listener hears it.
        var notifier = app.Services.GetRequiredService<IIpChangeNotifier>();
        var publisher = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                notifier.Publish(new IpChangedEventArgs
                {
                    NewIp = "198.51.100.7",
                    IpCheck = new IpCheck { IpAddress = "198.51.100.7", LastConfirmedAt = DateTime.UtcNow }
                });
                await Task.Delay(50, cancel.Token).ContinueWith(_ => { });
            }
        });

        var lines = new List<string>();
        while (lines.Count < 4)
        {
            var line = await reader.ReadLineAsync(cancel.Token);
            if (!string.IsNullOrEmpty(line)) lines.Add(line);
        }

        await cancel.CancelAsync();
        await publisher;
        Assert.Equal("event: ip-current", lines[0]);
        Assert.Equal("event: ip-changed", lines[2]);
        Assert.Contains("\"ipAddress\":\"198.51.100.7\"", lines[3]);
    }

    private sealed class ExplodingIpCheckReader : IIpCheckReader
    {
        public Task<IpCheck?> GetLatestAsync() => throw new InvalidOperationException("secret internal detail");
        public Task<List<IpCheck>> GetRecentAsync(int count = 100) => throw new InvalidOperationException();
        public Task<IpCheck?> GetByIdAsync(int id) => throw new InvalidOperationException();
        public Task<List<IpCheckGroup>> GetGroupedByIpAsync() => throw new InvalidOperationException();
    }
}
