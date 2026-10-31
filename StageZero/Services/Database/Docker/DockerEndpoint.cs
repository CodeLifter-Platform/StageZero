using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StageZero.Services.Database.Docker;

/// <summary>
/// Where the Docker engine's Unix socket is, if StageZero can see one: <c>DOCKER_HOST</c>
/// when it names a <c>unix://</c> socket, else the Docker CLI's current context (the one
/// <c>docker</c> itself would use), else the standard paths of the engines people run on a
/// workstation (Docker Engine and Desktop, BoxCar, Colima, OrbStack, Rancher Desktop). In a
/// container there is one only when the compose overlay mounted it — a deliberate grant,
/// since whoever holds the socket holds the host.
/// </summary>
public static class DockerEndpoint
{
    public const string HostVariable = "DOCKER_HOST";
    public const string DefaultSocket = "/var/run/docker.sock";
    private const string UnixScheme = "unix://";

    public static string? Discover()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Discover(Environment.GetEnvironmentVariable(HostVariable), home, File.Exists,
            path => File.Exists(path) ? File.ReadAllText(path) : null);
    }

    public static string? Discover(string? dockerHost, string home, Func<string, bool> exists, Func<string, string?> read)
    {
        if (!string.IsNullOrWhiteSpace(dockerHost))
        {
            return UnixPath(dockerHost) is { } fromEnvironment && exists(fromEnvironment)
                ? fromEnvironment
                : null; // tcp:// and npipe:// engines are not driven from here
        }

        if (CurrentContextSocket(home, read) is { } fromContext && exists(fromContext))
        {
            return fromContext;
        }

        return Candidates(home).FirstOrDefault(exists);
    }

    /// <summary>In the order a workstation most likely has them.</summary>
    public static IEnumerable<string> Candidates(string home)
    {
        yield return DefaultSocket;
        yield return Path.Combine(home, ".docker", "run", "docker.sock");
        yield return Path.Combine(home, ".boxcar", "docker.sock");
        yield return Path.Combine(home, ".colima", "default", "docker.sock");
        yield return Path.Combine(home, ".orbstack", "run", "docker.sock");
        yield return Path.Combine(home, ".rd", "docker.sock");
    }

    /// <summary>
    /// The socket of the CLI's current context: <c>~/.docker/config.json</c> names it, and
    /// <c>~/.docker/contexts/meta/&lt;sha256 of the name&gt;/meta.json</c> holds its endpoint.
    /// </summary>
    public static string? CurrentContextSocket(string home, Func<string, string?> read)
    {
        try
        {
            var config = read(Path.Combine(home, ".docker", "config.json"));
            if (config is null)
            {
                return null;
            }

            using var configDocument = JsonDocument.Parse(config);
            if (!configDocument.RootElement.TryGetProperty("currentContext", out var current)
                || current.GetString() is not { Length: > 0 } name || name == "default")
            {
                return null;
            }

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
            var meta = read(Path.Combine(home, ".docker", "contexts", "meta", hash, "meta.json"));
            if (meta is null)
            {
                return null;
            }

            using var metaDocument = JsonDocument.Parse(meta);
            return metaDocument.RootElement.TryGetProperty("Endpoints", out var endpoints)
                && endpoints.TryGetProperty("docker", out var docker)
                && docker.TryGetProperty("Host", out var host)
                ? UnixPath(host.GetString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? UnixPath(string? endpoint) =>
        endpoint is not null && endpoint.StartsWith(UnixScheme, StringComparison.OrdinalIgnoreCase) ? endpoint[UnixScheme.Length..] : null;
}
