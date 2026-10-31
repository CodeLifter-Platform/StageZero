using StageZero.Services.Database;
using StageZero.Services.Database.Docker;
using StageZero.Tests.Fakes;

namespace StageZero.Tests.Database;

public sealed class PostgresDiscoveryTests
{
    private readonly FakeProber _prober = new();
    private readonly FakeDocker _docker = new();

    private PostgresDiscovery OnHost() => new(_prober, _docker, new AppContainer("/tmp/sz", inContainer: false, hostname: null));

    [Fact]
    public async Task A_server_on_a_standard_port_is_found_and_the_next_port_is_suggested()
    {
        _prober.Listening.Add(("localhost", 5432));

        var found = await OnHost().DiscoverAsync();

        Assert.Equal([new DiscoveredServer("localhost", 5432, "this machine")], found.Servers);
        Assert.Equal(5433, found.SuggestedPort);
        Assert.Equal(8, _prober.Probed.Count); // 5432–5439, once each
        Assert.True(found.Docker.Available);
        Assert.Contains("28.0.0", found.Docker.Detail);
        Assert.Null(found.Container);
        Assert.Equal((Path.Combine("/tmp/sz", "postgres"), Path.Combine("/tmp/sz", "backups")), (found.DataFolder, found.BackupFolder));
    }

    [Fact]
    public async Task Nothing_found_means_the_default_port_and_an_empty_list()
    {
        var found = await OnHost().DiscoverAsync();

        Assert.Empty(found.Servers);
        Assert.Equal(5432, found.SuggestedPort);
    }

    [Fact]
    public async Task From_a_container_the_machine_is_the_Docker_host_and_the_missing_socket_names_the_overlay()
    {
        _docker.Engine = null;
        _prober.Listening.Add((PostgresDiscovery.HostFromContainer, 5433));
        var discovery = new PostgresDiscovery(_prober, _docker, new AppContainer("/app-data", inContainer: true, hostname: "abc"));

        var found = await discovery.DiscoverAsync();

        Assert.Equal([new DiscoveredServer(PostgresDiscovery.HostFromContainer, 5433, "this machine")], found.Servers);
        Assert.Equal(5432, found.SuggestedPort); // the machine's 5432 is free; only 5433 is taken
        Assert.False(found.Docker.Available);
        Assert.Contains("docker-compose.provision.yml", found.Docker.Detail);
        Assert.True(found.InContainer);
    }

    [Fact]
    public async Task StageZeros_own_container_is_reported_with_its_published_port()
    {
        _docker.Containers[PostgresProvisioner.ContainerName] = new ContainerInspection("x", PostgresProvisioner.ContainerName, "running", true,
            [], ["stagezero_default"], new Dictionary<string, int> { ["5432/tcp"] = 5434 }, []);

        var found = await OnHost().DiscoverAsync();

        Assert.Equal(new ExistingContainer(PostgresProvisioner.ContainerName, "running", true, 5434), found.Container);
    }

    [Fact]
    public async Task A_socket_nobody_mounted_and_a_socket_the_user_may_not_open_are_told_apart()
    {
        using var none = new DockerApi(null);
        Assert.Contains("docker-compose.provision.yml", await none.ExplainUnavailableAsync(inContainer: true));
        Assert.Contains("Is Docker running?", await none.ExplainUnavailableAsync(inContainer: false));

        Assert.Equal(
            "The Docker socket /var/run/docker.sock is mounted, but StageZero's container user may not use it. " +
            "It belongs to group 101: put DOCKER_GID=101 in .env and run the compose command again.",
            DockerApi.PermissionAdvice("/var/run/docker.sock", 101, inContainer: true));
        Assert.Contains("add your user to that group", DockerApi.PermissionAdvice("/var/run/docker.sock", 999, inContainer: false));
        Assert.Contains("Set DOCKER_GID", DockerApi.PermissionAdvice("/var/run/docker.sock", null, inContainer: true));
    }

    [Fact]
    public void The_Docker_socket_comes_from_DOCKER_HOST_the_CLI_context_or_the_usual_places()
    {
        var present = new HashSet<string> { "/Users/a/.colima/default/docker.sock", "/custom/docker.sock", "/Users/a/.boxcar/docker.sock" };
        static string? nothing(string path) => null;

        Assert.Equal("/custom/docker.sock", DockerEndpoint.Discover("unix:///custom/docker.sock", "/Users/a", present.Contains, nothing));
        Assert.Null(DockerEndpoint.Discover("tcp://127.0.0.1:2375", "/Users/a", present.Contains, nothing));
        Assert.Equal("/Users/a/.boxcar/docker.sock", DockerEndpoint.Discover(null, "/Users/a", present.Contains, nothing));
        Assert.Null(DockerEndpoint.Discover(null, "/Users/b", present.Contains, nothing));
        Assert.Equal(DockerEndpoint.DefaultSocket, DockerEndpoint.Candidates("/Users/a").First());

        // The CLI's current context wins over the well-known paths: here "boxcar" → its meta.json.
        // sha256("boxcar") names the folder, as the Docker CLI lays it out.
        var files = new Dictionary<string, string>
        {
            ["/Users/a/.docker/config.json"] = """{"currentContext":"boxcar"}""",
            ["/Users/a/.docker/contexts/meta/" + Sha("boxcar") + "/meta.json"] =
                """{"Name":"boxcar","Endpoints":{"docker":{"Host":"unix:///Users/a/.boxcar/docker.sock"}}}""",
        };
        Assert.Equal("/Users/a/.boxcar/docker.sock", DockerEndpoint.Discover(null, "/Users/a", present.Contains, files.GetValueOrDefault));
        Assert.Equal("/Users/a/.colima/default/docker.sock",
            DockerEndpoint.Discover(null, "/Users/a", p => present.Contains(p) && !p.Contains("boxcar"), files.GetValueOrDefault));
    }

    private static string Sha(string name) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
}
