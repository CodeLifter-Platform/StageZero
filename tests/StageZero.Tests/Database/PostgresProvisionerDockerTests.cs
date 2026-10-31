using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StageZero.Services.Database;
using StageZero.Services.Database.Docker;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Database;

/// <summary>
/// The real thing: a PostgreSQL container created through the engine, with the two
/// accounts the page promises, then removed. Its own container name and port, so it never
/// meets an install's stagezero-postgres.
/// </summary>
public sealed class PostgresProvisionerDockerTests : IAsyncLifetime
{
    private readonly string _appData = Directory.CreateTempSubdirectory("stagezero-docker-").FullName;
    private readonly string _name = $"stagezero-postgres-test-{Guid.NewGuid():N}"[..40];
    private DockerApi? _docker;

    [DockerFact]
    public async Task Creates_a_server_whose_two_accounts_hold_the_two_passwords()
    {
        var socket = DockerEndpoint.Discover();
        Assert.True(socket is not null, $"No Docker socket found; set {DockerEndpoint.HostVariable}=unix:///path/to/docker.sock");
        _docker = new DockerApi(socket);
        Assert.NotNull(await _docker.PingAsync());
        var provisioner = new PostgresProvisioner(_docker, new NpgsqlAdmin(),
            new AppContainer(_appData, inContainer: false, hostname: null), NullLogger<PostgresProvisioner>.Instance, _name);
        var port = FreePort();
        const string admin = "test-admin-password-1";
        const string app = "test-stagezero-pw-22";

        // Every interface: on some desktop engines a 127.0.0.1 publish can't be reached from the host.
        var created = await provisioner.CreateAsync(new CreatePostgresRequest(port, admin, app, null, PublishOnAllInterfaces: true));

        Assert.Equal((_name, "localhost", port, "stagezero", "stagezero"), (created.ContainerName, created.Host, created.Port, created.Database, created.Username));
        Assert.True(Directory.Exists(created.BackupFolder));

        await using (var asApp = new NpgsqlConnection($"Host=localhost;Port={port};Database=stagezero;Username=stagezero;Password={app};Pooling=false"))
        {
            await asApp.OpenAsync();
            await using var command = asApp.CreateCommand();
            command.CommandText = "SELECT current_user || '@' || current_database()";
            Assert.Equal("stagezero@stagezero", await command.ExecuteScalarAsync());
        }

        await using (var asAdmin = new NpgsqlConnection($"Host=localhost;Port={port};Database=postgres;Username=postgres;Password={admin};Pooling=false"))
        {
            await asAdmin.OpenAsync();
        }

        var inspected = await _docker.InspectContainerAsync(_name);
        Assert.True(inspected!.Running);
        Assert.Equal(port, inspected.PublishedPorts["5432/tcp"]);

        // The environment anyone with the socket can read holds only the bootstrap password,
        // and that one no longer opens the server (the published port is where SCRAM applies;
        // inside the image, localhost is trusted, so this is the only honest place to check).
        var bootstrap = Assert.Single(inspected.Env, e => e.StartsWith("POSTGRES_PASSWORD=", StringComparison.Ordinal))["POSTGRES_PASSWORD=".Length..];
        Assert.NotEqual(admin, bootstrap);
        Assert.DoesNotContain(inspected.Env, e => e.Contains(admin, StringComparison.Ordinal) || e.Contains(app, StringComparison.Ordinal));
        await using var asBootstrap = new NpgsqlConnection($"Host=localhost;Port={port};Database=postgres;Username=postgres;Password={bootstrap};Pooling=false");
        var refused = await Assert.ThrowsAsync<PostgresException>(() => asBootstrap.OpenAsync());
        Assert.Equal(PostgresErrorCodes.InvalidPassword, refused.SqlState);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_docker is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await _docker.RemoveContainerAsync(_name, force: true);
            _docker.Dispose();
        }

        try
        {
            Directory.Delete(_appData, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The server's files are owned by its own user on a Linux host; the temp cleaner has them.
        }
    }
}
