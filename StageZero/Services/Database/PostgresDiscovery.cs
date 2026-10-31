using System.Net.Sockets;
using Npgsql;
using StageZero.Services.Database.Docker;

namespace StageZero.Services.Database;

/// <summary>Answers whether something speaking the PostgreSQL protocol listens at host:port.</summary>
public interface IPostgresProber
{
    Task<bool> IsPostgresAsync(string host, int port, CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens a connection with a throwaway login. A PostgreSQL server answers that (usually by
/// refusing the password), which is all the probe wants to know; a closed port, a timeout,
/// or something that isn't PostgreSQL does not.
/// </summary>
public sealed class NpgsqlProber : IPostgresProber
{
    public async Task<bool> IsPostgresAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = host, Port = port, Username = "stagezero-probe", Password = "probe", Database = "postgres",
            Timeout = 2, Pooling = false, GssEncryptionMode = GssEncryptionMode.Disable
        }.ConnectionString;
        try
        {
            await using var probe = new NpgsqlConnection(connection);
            await probe.OpenAsync(cancellationToken);
            return true;
        }
        catch (PostgresException)
        {
            return true; // the server spoke: wrong password, no such role, no such database
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is NpgsqlException or SocketException or IOException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }
}

public sealed record DiscoveredServer(string Host, int Port, string Label);

public sealed record DockerAvailability(bool Available, string Detail);

public sealed record ExistingContainer(string Name, string Status, bool Running, int? HostPort);

public sealed record Discovery(
    IReadOnlyList<DiscoveredServer> Servers,
    DockerAvailability Docker,
    /// <summary>StageZero's own PostgreSQL container, if one exists already.</summary>
    ExistingContainer? Container,
    int SuggestedPort,
    /// <summary>Host paths: where a new container would keep its data, and the default for its backups.</summary>
    string DataFolder,
    string BackupFolder,
    bool InContainer);

/// <summary>
/// What a "Use PostgreSQL" click learns about the machine: servers answering on the standard
/// ports (5432–5439), whether Docker is there to create one, and whether StageZero already
/// created one. From inside a container the machine is reached as <c>host.docker.internal</c>,
/// which the compose files map to the host.
/// </summary>
public sealed class PostgresDiscovery
{
    public const int FirstPort = 5432;
    public const int LastPort = 5439;
    public const string HostFromContainer = "host.docker.internal";

    private readonly IPostgresProber _prober;
    private readonly IDockerApi _docker;
    private readonly AppContainer _self;

    public PostgresDiscovery(IPostgresProber prober, IDockerApi docker, AppContainer self)
    {
        _prober = prober;
        _docker = docker;
        _self = self;
    }

    /// <summary>The hosts a probe tries, with how the page names them.</summary>
    public static IReadOnlyList<(string Host, string Label)> Hosts(bool inContainer) => inContainer
        ?
        [
            (HostFromContainer, "this machine"),
            (PostgresProvisioner.ContainerName, "StageZero's own container"),
            ("postgres", "the compose add-on"),
        ]
        : [("localhost", "this machine")];

    public async Task<Discovery> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var inContainer = _self.InContainer;
        var probes = Hosts(inContainer)
            .SelectMany(h => Enumerable.Range(FirstPort, LastPort - FirstPort + 1).Select(port => (h.Host, h.Label, Port: port)))
            .Select(async p => await _prober.IsPostgresAsync(p.Host, p.Port, cancellationToken) ? new DiscoveredServer(p.Host, p.Port, p.Label) : null)
            .ToList();
        var servers = (await Task.WhenAll(probes)).OfType<DiscoveredServer>().ToList();

        var engine = await _docker.PingAsync(cancellationToken);
        ExistingContainer? container = null;
        if (engine is not null)
        {
            var existing = await _docker.InspectContainerAsync(PostgresProvisioner.ContainerName, cancellationToken);
            if (existing is not null)
            {
                container = new ExistingContainer(existing.Name, existing.Status, existing.Running,
                    existing.PublishedPorts.TryGetValue("5432/tcp", out var hostPort) ? hostPort : null);
            }
        }

        var docker = engine is null
            ? new DockerAvailability(false, await _docker.ExplainUnavailableAsync(inContainer, cancellationToken))
            : new DockerAvailability(true, $"Docker {engine.Version} via {engine.Socket}");

        var hostDataDirectory = await _self.HostDataDirectoryAsync(_docker, cancellationToken);
        return new Discovery(
            servers,
            docker,
            container,
            SuggestPort(servers.Where(s => s.Label == "this machine").Select(s => s.Port)),
            Path.Combine(hostDataDirectory, PostgresProvisioner.DataFolderName),
            Path.Combine(hostDataDirectory, PostgresProvisioner.BackupFolderName),
            inContainer);
    }

    /// <summary>The default port, unless something on the machine already answers there.</summary>
    public static int SuggestPort(IEnumerable<int> taken)
    {
        var set = taken.ToHashSet();
        for (var port = FirstPort; port <= LastPort; port++)
        {
            if (!set.Contains(port))
            {
                return port;
            }
        }

        return LastPort + 1;
    }
}

/// <summary>
/// StageZero's own container, when it runs in one: its name on the Docker network, and the
/// host path behind <c>/app-data</c>, which is where a new PostgreSQL container's folders go
/// so they are bind mounts the compose files never own.
/// </summary>
public sealed class AppContainer
{
    private readonly string _appDataDirectory;
    private string? _hostDataDirectory;

    public AppContainer(string appDataDirectory, bool inContainer, string? hostname)
    {
        _appDataDirectory = appDataDirectory;
        InContainer = inContainer;
        Hostname = hostname;
    }

    public static AppContainer Detect() =>
        new(DataPathService.GetAppDataDirectory(), DataPathService.IsRunningInContainer(), Environment.GetEnvironmentVariable("HOSTNAME"));

    public bool InContainer { get; }

    /// <summary>Docker sets the container's hostname to its short id, which the engine accepts as a name.</summary>
    public string? Hostname { get; }

    /// <summary>The data directory as this process sees it.</summary>
    public string LocalDataDirectory => _appDataDirectory;

    /// <summary>The data directory as the host sees it: itself on a host, the mount's source in a container.</summary>
    public async Task<string> HostDataDirectoryAsync(IDockerApi docker, CancellationToken cancellationToken = default)
    {
        if (_hostDataDirectory is not null)
        {
            return _hostDataDirectory;
        }

        if (!InContainer)
        {
            return _hostDataDirectory = _appDataDirectory;
        }

        var self = Hostname is null ? null : await TryInspectAsync(docker, Hostname, cancellationToken);
        var mount = self?.Mounts.FirstOrDefault(m => m.Destination == _appDataDirectory);
        return _hostDataDirectory = mount?.Source ?? _appDataDirectory;
    }

    /// <summary>The Docker network this container is on, so a new one can join it; null on a host.</summary>
    public async Task<string?> NetworkAsync(IDockerApi docker, CancellationToken cancellationToken = default)
    {
        if (!InContainer || Hostname is null)
        {
            return null;
        }

        var self = await TryInspectAsync(docker, Hostname, cancellationToken);
        return self?.Networks.FirstOrDefault(n => n != "bridge") ?? self?.Networks.FirstOrDefault();
    }

    private static async Task<ContainerInspection?> TryInspectAsync(IDockerApi docker, string hostname, CancellationToken cancellationToken)
    {
        try
        {
            return await docker.InspectContainerAsync(hostname, cancellationToken);
        }
        catch (Exception ex) when (ex is DockerException or HttpRequestException or SocketException or IOException)
        {
            return null;
        }
    }
}
