namespace StageZero.Services.Cli;

/// <summary>
/// <c>StageZero healthcheck</c>: asks the running app's <c>/healthz</c> and exits 0 if it
/// answers 200. The container's HEALTHCHECK — the runtime image has no curl or wget. It
/// inherits the container's environment, so it finds the port the app listens on.
/// </summary>
public static class HealthCheckCommand
{
    public const string Name = "healthcheck";
    public const string Path = "/healthz";

    public static async Task<int> RunAsync(IReadOnlyDictionary<string, string?> environment, HttpMessageHandler? handler = null)
    {
        using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(5);
        try
        {
            using var response = await client.GetAsync(new Uri(LocalBaseUrl(environment), Path));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }

    /// <summary>
    /// Where the app listens, from inside its own container: the first http:// binding in
    /// ASPNETCORE_URLS, else ASPNETCORE_HTTP_PORTS, else 8080 (the .NET image default).
    /// Wildcard hosts become localhost.
    /// </summary>
    public static Uri LocalBaseUrl(IReadOnlyDictionary<string, string?> environment)
    {
        var urls = environment.GetValueOrDefault("ASPNETCORE_URLS");
        var http = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (http is not null)
        {
            var port = http.LastIndexOf(':') is var colon && colon > "http://".Length
                && int.TryParse(http[(colon + 1)..].TrimEnd('/'), out var p) ? p : 80;
            return new Uri($"http://localhost:{port}");
        }

        var ports = environment.GetValueOrDefault("ASPNETCORE_HTTP_PORTS")?.Split(';', ',')[0].Trim();
        return new Uri($"http://localhost:{(int.TryParse(ports, out var httpPort) ? httpPort : 8080)}");
    }
}
