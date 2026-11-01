using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StageZero.Services;
using StageZero.Services.Database;
using StageZero.Services.Database.Docker;
using StageZero.Tests.Fakes;

namespace StageZero.Tests.Database;

/// <summary>
/// Creating StageZero's own PostgreSQL container, against an engine in memory: what is asked
/// of Docker, which passwords end up where, and what happens when a step fails.
/// </summary>
public sealed class PostgresProvisionerTests : IDisposable
{
    private const string Admin = "admin-password-123456";
    private const string App = "stagezero-password-1234";

    private readonly string _appData = Directory.CreateTempSubdirectory("stagezero-provision-").FullName;
    private readonly FakeDocker _docker = new();
    private readonly FakeAdmin _sql = new();

    private PostgresProvisioner OnHost() =>
        new(_docker, _sql, new AppContainer(_appData, inContainer: false, hostname: null), NullLogger<PostgresProvisioner>.Instance);

    private static CreatePostgresRequest Request(int port = 5433, string admin = Admin, string app = App, string? backups = null, bool everywhere = false) =>
        new(port, admin, app, backups, everywhere);

    [Fact]
    public async Task Creates_the_container_with_its_folders_beside_the_data_directory_and_sets_both_passwords()
    {
        var created = await OnHost().CreateAsync(Request());

        var (name, spec) = Assert.Single(_docker.Created);
        Assert.Equal(PostgresProvisioner.ContainerName, name);
        Assert.Equal(PostgresProvisioner.Image, spec.Image);
        Assert.Equal([PostgresProvisioner.Image], _docker.Pulled);
        Assert.Contains((Path.Combine(_appData, "postgres"), "/var/lib/postgresql"), spec.Binds);
        Assert.Contains((Path.Combine(_appData, "backups"), "/backups"), spec.Binds);
        Assert.True(Directory.Exists(Path.Combine(_appData, "postgres")));
        Assert.True(Directory.Exists(Path.Combine(_appData, "backups")));
        Assert.Equal([(5432, "127.0.0.1", 5433)], spec.Ports);
        Assert.Equal("unless-stopped", spec.RestartPolicy);
        Assert.Null(spec.Network);
        Assert.Equal(["id-stagezero-postgres"], _docker.Started);

        // The operator's passwords are set over the wire, never handed to the image.
        var bootstrap = Assert.Single(spec.Env, e => e.StartsWith("POSTGRES_PASSWORD=", StringComparison.Ordinal))["POSTGRES_PASSWORD=".Length..];
        Assert.NotEqual(Admin, bootstrap);
        Assert.NotEqual(App, bootstrap);
        Assert.Equal(3, _sql.Statements.Count);
        Assert.Equal($"ALTER USER postgres PASSWORD '{Admin}';", _sql.Statements[0].Sql);
        Assert.Equal(bootstrap, new NpgsqlConnectionStringBuilder(_sql.Statements[0].ConnectionString).Password);
        Assert.Equal($"CREATE ROLE stagezero LOGIN PASSWORD '{App}';", _sql.Statements[1].Sql);
        Assert.Equal(Admin, new NpgsqlConnectionStringBuilder(_sql.Statements[1].ConnectionString).Password);
        Assert.Equal("CREATE DATABASE stagezero OWNER stagezero;", _sql.Statements[2].Sql);

        Assert.Equal(("localhost", 5433, 5433, "stagezero", "stagezero"), (created.Host, created.Port, created.HostPort, created.Database, created.Username));
        Assert.Equal(Path.Combine(_appData, "backups"), created.BackupFolder);
    }

    [Fact]
    public async Task Inside_a_container_the_folders_are_host_paths_and_the_new_server_joins_the_stack_network()
    {
        _docker.Containers["abc123"] = new ContainerInspection("abc123", "stagezero", "running", true,
            [new ContainerMount(_appData, "/srv/stagezero-data", "bind")], ["stagezero_default"], new Dictionary<string, int>(), []);
        var provisioner = new PostgresProvisioner(_docker, _sql,
            new AppContainer(_appData, inContainer: true, hostname: "abc123"), NullLogger<PostgresProvisioner>.Instance);

        var created = await provisioner.CreateAsync(Request(backups: "/mnt/backups/stagezero", everywhere: true));

        var spec = Assert.Single(_docker.Created).Spec;
        Assert.Equal("stagezero_default", spec.Network);
        Assert.Contains(("/srv/stagezero-data/postgres", "/var/lib/postgresql"), spec.Binds);
        Assert.Contains(("/mnt/backups/stagezero", "/backups"), spec.Binds);
        Assert.Equal([(5432, "0.0.0.0", 5433)], spec.Ports);
        // The app reaches it by name on the network, on the container's own port.
        Assert.Equal(("stagezero-postgres", 5432, 5433), (created.Host, created.Port, created.HostPort));
        Assert.Equal("stagezero-postgres", new NpgsqlConnectionStringBuilder(_sql.Statements[0].ConnectionString).Host);
        // The data folder was created through this side of the mount; the backups folder is Docker's to make.
        Assert.True(Directory.Exists(Path.Combine(_appData, "postgres")));
    }

    [Theory]
    [InlineData("short", App, "at least 12")]
    [InlineData(Admin, "has a space in it", "no spaces")]
    [InlineData(Admin, "semi;colon-password", "can't contain")]
    [InlineData(Admin, Admin, "two different passwords")]
    [InlineData("", App, "Enter the admin password")]
    public async Task An_unusable_password_is_refused_before_anything_is_created(string admin, string app, string reason)
    {
        var error = await Assert.ThrowsAsync<UserFacingException>(() => OnHost().CreateAsync(Request(admin: admin, app: app)));

        Assert.Contains(reason, error.Message);
        Assert.Empty(_docker.Created);
    }

    [Fact]
    public async Task A_relative_backups_folder_and_a_privileged_port_are_refused()
    {
        Assert.Contains("full path", (await Assert.ThrowsAsync<UserFacingException>(() => OnHost().CreateAsync(Request(backups: "backups")))).Message);
        Assert.Contains("between 1024", (await Assert.ThrowsAsync<UserFacingException>(() => OnHost().CreateAsync(Request(port: 80)))).Message);
        Assert.Empty(_docker.Created);
    }

    [Fact]
    public async Task An_existing_container_of_that_name_is_a_conflict_that_names_it()
    {
        _docker.Containers[PostgresProvisioner.ContainerName] =
            new ContainerInspection("old", PostgresProvisioner.ContainerName, "exited", false, [], [], new Dictionary<string, int>(), []);

        var error = await Assert.ThrowsAsync<UserFacingException>(() => OnHost().CreateAsync(Request()));

        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.Contains("exited", error.Message);
        Assert.Contains("docker rm -f stagezero-postgres", error.Message);
    }

    [Fact]
    public async Task A_data_folder_that_already_holds_a_cluster_is_refused_before_anything_is_created()
    {
        Directory.CreateDirectory(Path.Combine(_appData, "postgres", "18"));
        File.WriteAllText(Path.Combine(_appData, "postgres", "18", "PG_VERSION"), "18");

        var error = await Assert.ThrowsAsync<UserFacingException>(() => OnHost().CreateAsync(Request()));

        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.Contains("already holds a PostgreSQL cluster", error.Message);
        Assert.Contains("docker start stagezero-postgres", error.Message);
        Assert.Empty(_docker.Pulled);
        Assert.Empty(_docker.Created);
    }

    [Fact]
    public async Task Without_an_engine_nothing_is_attempted()
    {
        _docker.Engine = null;

        var error = await Assert.ThrowsAsync<UserFacingException>(() => OnHost().CreateAsync(Request()));

        Assert.Contains("Docker engine", error.Message);
        Assert.Empty(_docker.Pulled);
    }

    [Fact]
    public async Task A_refusal_from_Docker_is_passed_on_in_its_words()
    {
        _docker.CreateRefusal = new DockerException(500, "driver failed programming external connectivity: port is already allocated");

        var error = await Assert.ThrowsAsync<UserFacingException>(() => OnHost().CreateAsync(Request()));

        Assert.Contains("port is already allocated", error.Message);
        Assert.Empty(_docker.Started);
    }

    [Fact]
    public async Task A_server_that_never_comes_up_is_removed_and_the_folders_are_left_for_inspection()
    {
        _sql.NeverReady = true;

        var error = await Assert.ThrowsAsync<UserFacingException>(() => OnHost().CreateAsync(Request()));

        Assert.Equal(["id-stagezero-postgres"], _docker.Removed);
        Assert.Empty(_docker.Containers);
        Assert.Contains("90 seconds", error.Message);
        Assert.Contains(Path.Combine(_appData, "postgres"), error.Message);
        Assert.Empty(_sql.Statements);
    }

    [Fact]
    public void The_spec_serialises_the_way_the_engine_expects()
    {
        var spec = new ContainerSpec("postgres:18-alpine", ["A=1"], new Dictionary<string, string> { ["l"] = "v" },
            [("/host/data", "/var/lib/postgresql")], [(5432, "127.0.0.1", 5433)], "stagezero_default", "unless-stopped");

        var json = spec.ToJson();

        Assert.Equal("/host/data:/var/lib/postgresql", json["HostConfig"]!["Binds"]![0]!.GetValue<string>());
        Assert.Equal("5433", json["HostConfig"]!["PortBindings"]!["5432/tcp"]![0]!["HostPort"]!.GetValue<string>());
        Assert.Equal("127.0.0.1", json["HostConfig"]!["PortBindings"]!["5432/tcp"]![0]!["HostIp"]!.GetValue<string>());
        Assert.Equal("stagezero_default", json["HostConfig"]!["NetworkMode"]!.GetValue<string>());
        Assert.Equal("unless-stopped", json["HostConfig"]!["RestartPolicy"]!["Name"]!.GetValue<string>());
        Assert.NotNull(json["ExposedPorts"]!["5432/tcp"]);
        Assert.Equal("v", json["Labels"]!["l"]!.GetValue<string>());
    }

    [Fact]
    public void An_inspect_document_yields_the_mounts_networks_and_published_ports()
    {
        const string sample = """
            {"Id":"abc","Name":"/stagezero","State":{"Status":"running","Running":true},
             "Mounts":[{"Type":"bind","Source":"/srv/data","Destination":"/app-data"}],
             "NetworkSettings":{"Networks":{"stagezero_default":{}},"Ports":{"8080/tcp":[{"HostIp":"0.0.0.0","HostPort":"5100"}],"9/tcp":null}}}
            """;
        using var document = System.Text.Json.JsonDocument.Parse(sample);

        var inspection = DockerApi.Parse(document.RootElement);

        Assert.Equal(("abc", "stagezero", "running", true), (inspection.Id, inspection.Name, inspection.Status, inspection.Running));
        Assert.Equal([new ContainerMount("/app-data", "/srv/data", "bind")], inspection.Mounts);
        Assert.Equal(["stagezero_default"], inspection.Networks);
        Assert.Equal(5100, inspection.PublishedPorts["8080/tcp"]);
    }

    public void Dispose() => Directory.Delete(_appData, recursive: true);
}
