using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Services.IpMonitoring;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.IpMonitoring;

/// <summary>
/// The public IP moves every DNS record, so it takes two sources agreeing to change it, and
/// nothing that isn't a public IPv4 address counts as an answer.
/// </summary>
public class PublicIpResolverTests
{
    private const string Cloudflare = "https://1.1.1.1/cdn-cgi/trace";
    private const string Ipify = "https://api.ipify.org";
    private const string Aws = "https://checkip.amazonaws.com";

    private readonly FakeHttp _http = new();

    private PublicIpResolver Resolver() => new(new FakeHttpClientFactory(_http), NullLogger<PublicIpResolver>.Instance);

    private void Answer(string url, string body) => _http.OnGet(url, body);

    private void Down(string url) => _http.OnGet(url, "Service Unavailable", HttpStatusCode.ServiceUnavailable);

    [Fact]
    public async Task All_sources_agreeing_decides_it()
    {
        _http.PublicIp("203.0.113.9");

        Assert.Equal("203.0.113.9", await Resolver().ResolveAsync(lastKnown: null));
    }

    [Fact]
    public async Task A_captive_portal_page_is_outvoted()
    {
        Answer(Cloudflare, "fl=1\nip=203.0.113.9\nts=1\n");
        Answer(Ipify, "<html><body>Please log in to the hotel Wi-Fi</body></html>");
        Answer(Aws, "203.0.113.9\n");

        Assert.Equal("203.0.113.9", await Resolver().ResolveAsync(lastKnown: "198.51.100.1"));
    }

    [Fact]
    public async Task The_majority_wins_a_disagreement()
    {
        Answer(Cloudflare, "ip=203.0.113.9");
        Answer(Ipify, "198.51.100.77");
        Answer(Aws, "203.0.113.9");

        Assert.Equal("203.0.113.9", await Resolver().ResolveAsync(lastKnown: null));
    }

    [Fact]
    public async Task One_source_alone_cannot_move_the_ip()
    {
        Answer(Cloudflare, "ip=203.0.113.9");
        Down(Ipify);
        Down(Aws);

        await Assert.ThrowsAsync<PublicIpUnresolvedException>(() => Resolver().ResolveAsync(lastKnown: "198.51.100.1"));
    }

    [Fact]
    public async Task One_source_alone_can_confirm_nothing_changed()
    {
        Answer(Cloudflare, "ip=203.0.113.9");
        Down(Ipify);
        Down(Aws);

        Assert.Equal("203.0.113.9", await Resolver().ResolveAsync(lastKnown: "203.0.113.9"));
    }

    [Fact]
    public async Task No_usable_answer_is_an_error_not_a_guess()
    {
        Answer(Cloudflare, "<html>");
        Down(Ipify);
        Answer(Aws, "");

        await Assert.ThrowsAsync<PublicIpUnresolvedException>(() => Resolver().ResolveAsync(lastKnown: null));
    }

    [Theory]
    [InlineData("203.0.113.9", "203.0.113.9")]
    [InlineData(" 203.0.113.9 \n", "203.0.113.9")]
    [InlineData("fl=12\nh=1.1.1.1\nip=203.0.113.9\nts=1\n", "203.0.113.9")]
    [InlineData("<html>203.0.113.9</html>", null)]
    [InlineData("192.168.1.10", null)]
    [InlineData("10.0.0.1", null)]
    [InlineData("172.20.0.1", null)]
    [InlineData("100.64.0.1", null)]
    [InlineData("127.0.0.1", null)]
    [InlineData("169.254.1.1", null)]
    [InlineData("0.0.0.0", null)]
    [InlineData("255.255.255.255", null)]
    [InlineData("2001:db8::1", null)]
    [InlineData("12345", null)]
    [InlineData("", null)]
    public void Only_a_public_IPv4_address_counts(string body, string? expected)
    {
        Assert.Equal(expected, PublicIpResolver.Parse(body));
    }

    private sealed class FakeHttpClientFactory(FakeHttp http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(http.CreateHandler());
    }
}
