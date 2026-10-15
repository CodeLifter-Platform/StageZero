using System.Net;
using System.Text;

namespace StageZero.Tests.Infrastructure;

/// <summary>
/// Stands in for the internet (ipify, Cloudflare) behind every HttpClient the app creates.
/// Routes match on method and URL prefix, newest first; an unmatched request fails the test,
/// so nothing here can ever reach a real API.
/// </summary>
public sealed class FakeHttp
{
    private readonly List<Route> _routes = [];
    private readonly List<Recorded> _requests = [];

    public IReadOnlyList<Recorded> Requests
    {
        get { lock (_requests) { return _requests.ToList(); } }
    }

    public FakeHttp On(HttpMethod method, string urlPrefix, Func<Recorded, HttpResponseMessage> respond)
    {
        lock (_routes)
        {
            _routes.Insert(0, new Route(method, urlPrefix, respond));
        }

        return this;
    }

    public FakeHttp OnGet(string urlPrefix, string body, HttpStatusCode status = HttpStatusCode.OK) =>
        On(HttpMethod.Get, urlPrefix, _ => Text(body, status));

    /// <summary>Every public-IP source answers with <paramref name="ip"/>.</summary>
    public FakeHttp PublicIp(string ip)
    {
        foreach (var (_, url) in StageZero.Services.IpMonitoring.PublicIpResolver.Sources)
        {
            OnGet(url, url.Contains("cdn-cgi/trace", StringComparison.Ordinal) ? $"fl=1\nh=1.1.1.1\nip={ip}\nts=1\n" : ip + "\n");
        }

        return this;
    }

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public HttpMessageHandler CreateHandler() => new Handler(this);

    public sealed record Recorded(HttpMethod Method, Uri Url, string? Body, string? Authorization);

    private sealed record Route(HttpMethod Method, string UrlPrefix, Func<Recorded, HttpResponseMessage> Respond);

    private sealed class Handler(FakeHttp http) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var recorded = new Recorded(
                request.Method,
                request.RequestUri!,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString());
            lock (http._requests)
            {
                http._requests.Add(recorded);
            }

            Route? route;
            lock (http._routes)
            {
                route = http._routes.FirstOrDefault(r =>
                    r.Method == request.Method
                    && request.RequestUri!.ToString().StartsWith(r.UrlPrefix, StringComparison.Ordinal));
            }

            return route is null
                ? throw new InvalidOperationException($"No fake response for {request.Method} {request.RequestUri}")
                : route.Respond(recorded);
        }
    }
}
