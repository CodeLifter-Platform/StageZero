using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StageZero.Services.Cli;
using StageZero.Services.IpMonitoring;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests;

[Collection(AppCollection.Name)]
public class HealthEndpointTests
{
    [Fact]
    public async Task A_running_app_is_healthy_to_an_anonymous_caller()
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateNonRedirectingClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_stale_ip_check_is_degraded_but_still_answers_200()
    {
        await using var app = new StageZeroApp();
        // Started 30 minutes ago and has never confirmed an IP since.
        var status = new IpMonitorStatus(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-30)));
        await using var degradedApp = app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IIpMonitorStatus>(status)));
        using var degradedClient = degradedApp.CreateClient();

        var response = await degradedClient.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_failed_check_is_recorded_for_the_health_check()
    {
        await using var app = new StageZeroApp();
        app.Http.OnGet("https://1.1.1.1/cdn-cgi/trace", "<html>");
        app.Http.OnGet("https://api.ipify.org", "<html>");
        app.Http.OnGet("https://checkip.amazonaws.com", "<html>");

        using (var scope = app.Services.CreateScope())
        {
            await Assert.ThrowsAsync<IpMonitorServiceException>(
                () => scope.ServiceProvider.GetRequiredService<IIpMonitorService>().CheckIpAsync());
        }

        var status = app.Services.GetRequiredService<IIpMonitorStatus>();
        Assert.NotNull(status.LastAttemptAt);
        Assert.Null(status.LastSuccessAt);
        Assert.Contains("usable answer", status.LastError);
    }
}

public class HealthCheckCommandTests
{
    [Theory]
    [InlineData(null, null, "http://localhost:8080/")]
    [InlineData("http://+:80", null, "http://localhost/")]
    [InlineData("http://*:5100", null, "http://localhost:5100/")]
    [InlineData("https://+:443;http://0.0.0.0:5000", null, "http://localhost:5000/")]
    [InlineData("http://localhost:6000/", null, "http://localhost:6000/")]
    [InlineData(null, "9090", "http://localhost:9090/")]
    [InlineData("https://+:443", "8081", "http://localhost:8081/")]
    public void It_finds_the_port_the_app_listens_on(string? urls, string? ports, string expected)
    {
        var environment = new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = urls, ["ASPNETCORE_HTTP_PORTS"] = ports };

        Assert.Equal(expected, HealthCheckCommand.LocalBaseUrl(environment).ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, 0)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 1)]
    public async Task It_exits_zero_only_on_a_healthy_answer(HttpStatusCode status, int expectedExit)
    {
        var http = new FakeHttp().OnGet("http://localhost:8080/healthz", "x", status);

        Assert.Equal(expectedExit, await HealthCheckCommand.RunAsync(new Dictionary<string, string?>(), http.CreateHandler()));
    }

    [Fact]
    public async Task It_exits_one_when_nothing_answers()
    {
        var nothing = new FakeHttp().On(HttpMethod.Get, "http://", _ => throw new HttpRequestException("refused"));

        Assert.Equal(1, await HealthCheckCommand.RunAsync(new Dictionary<string, string?>(), nothing.CreateHandler()));
    }
}
