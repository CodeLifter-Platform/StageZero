namespace StageZero.Api;

/// <summary>
/// Whether the host was told to listen on HTTPS, so that HTTPS redirection has somewhere to
/// send a request. <c>dotnet run</c> and the VS Code launch name an <c>https://</c> URL
/// (launchSettings.json / <c>ASPNETCORE_URLS</c>); the containers listen on plain HTTP only —
/// release because Cloudflare terminates TLS, debug because it is localhost — and
/// redirecting there would be a redirect to a listener that does not exist.
/// </summary>
public static class HttpsListener
{
    public static bool IsConfigured(IConfiguration configuration)
    {
        var urls = configuration[WebHostDefaults.ServerUrlsKey] ?? "";
        if (urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // ASPNETCORE_HTTPS_PORTS, the other way to ask for an HTTPS listener.
        return !string.IsNullOrWhiteSpace(configuration["HTTPS_PORTS"]);
    }
}
