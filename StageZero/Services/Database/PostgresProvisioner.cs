using System.Security.Cryptography;
using Npgsql;
using StageZero.Services.Database.Docker;

namespace StageZero.Services.Database;

/// <summary>Runs statements as the new server's superuser while it is being set up.</summary>
public interface IPostgresAdmin
{
    Task WaitUntilReadyAsync(string connectionString, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>One statement, outside any transaction (CREATE DATABASE refuses one).</summary>
    Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken = default);
}

public sealed class NpgsqlAdmin : IPostgresAdmin
{
    public async Task WaitUntilReadyAsync(string connectionString, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidPassword)
            {
                // The server is up and says no: waiting longer won't change its mind. The one
                // way this happens is a data folder that already held a cluster, whose
                // superuser password is not the bootstrap one the new container was given.
                throw new InvalidOperationException(
                    "The server came up but refused the password it was started with, so its data folder already held a " +
                    "PostgreSQL cluster from before. Move or delete that folder, or point the form at the existing server instead.", ex);
            }
            catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException or IOException or TimeoutException)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        throw new TimeoutException($"PostgreSQL did not accept a connection within {timeout.TotalSeconds:0} seconds.", last);
    }

    public async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record CreatePostgresRequest(
    int Port,
    string AdminPassword,
    string AppPassword,
    /// <summary>Host path for backups; null for the default beside the data directory.</summary>
    string? BackupFolder,
    /// <summary>Publish the port on every interface, not only 127.0.0.1.</summary>
    bool PublishOnAllInterfaces);

public sealed record CreatedPostgres(
    string ContainerName,
    /// <summary>How StageZero reaches it: the container's name on the shared network, or localhost.</summary>
    string Host,
    int Port,
    /// <summary>The port published on the host, for tools.</summary>
    int HostPort,
    string Database,
    string Username,
    string DataFolder,
    string BackupFolder);

/// <summary>
/// Creates StageZero's own PostgreSQL container: the official PostgreSQL 18 image, its data
/// and a backups folder as bind mounts beside StageZero's data directory (host folders that
/// no compose command owns, so <c>down</c>, <c>down -v</c> and <c>prune</c> leave them
/// alone), the port of the operator's choice, and two accounts with the operator's two
/// passwords: the <c>postgres</c> superuser and the <c>stagezero</c> role that owns the one
/// database and is all the app ever connects as.
/// <para>
/// The image needs a superuser password in its environment to initialise, and a container's
/// environment is readable by anyone with the Docker socket. So the container is started
/// with a random bootstrap password that is replaced by the operator's admin password the
/// moment the server is up, and never written anywhere. Neither password is stored by
/// StageZero: the operator is told to record both, and the app keeps only the connection
/// string it is later asked to switch to.
/// </para>
/// </summary>
public sealed class PostgresProvisioner
{
    public const string ContainerName = "stagezero-postgres";
    public const string Image = "postgres:18-alpine";
    public const string DataFolderName = "postgres";
    public const string BackupFolderName = "backups";
    public const string DatabaseName = "stagezero";
    public const string RoleName = "stagezero";
    public const string Label = "net.codelifter.stagezero";
    public const int MinPasswordLength = 12;
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(90);

    private readonly IDockerApi _docker;
    private readonly IPostgresAdmin _admin;
    private readonly AppContainer _self;
    private readonly ILogger<PostgresProvisioner> _logger;

    private readonly string _containerName;

    public PostgresProvisioner(IDockerApi docker, IPostgresAdmin admin, AppContainer self, ILogger<PostgresProvisioner> logger)
        : this(docker, admin, self, logger, ContainerName)
    {
    }

    /// <param name="containerName">Only the tests name it otherwise, so they never touch a real install's container.</param>
    public PostgresProvisioner(IDockerApi docker, IPostgresAdmin admin, AppContainer self, ILogger<PostgresProvisioner> logger, string containerName)
    {
        _docker = docker;
        _admin = admin;
        _self = self;
        _logger = logger;
        _containerName = containerName;
    }

    /// <summary>
    /// A password the server, the connection string and a shell all take without quoting
    /// trouble: printable ASCII, no spaces, no ';' (ends a connection string field) and no
    /// quotes, at least <see cref="MinPasswordLength"/> characters.
    /// </summary>
    public static string? PasswordProblem(string password, string which)
    {
        if (string.IsNullOrEmpty(password))
        {
            return $"Enter the {which} password.";
        }

        if (password.Length < MinPasswordLength)
        {
            return $"The {which} password needs at least {MinPasswordLength} characters.";
        }

        if (password.Any(c => c <= ' ' || c > '~'))
        {
            return $"The {which} password must be printable ASCII with no spaces.";
        }

        if (password.IndexOfAny([';', '\'', '"', '\\']) >= 0)
        {
            return $"The {which} password can't contain ; ' \" or \\ (they break the connection string).";
        }

        return null;
    }

    public async Task<CreatedPostgres> CreateAsync(CreatePostgresRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);

        if (await _docker.PingAsync(cancellationToken) is null)
        {
            throw new UserFacingException("StageZero can't reach the Docker engine. Look for servers again to see why.");
        }

        var existing = await _docker.InspectContainerAsync(_containerName, cancellationToken);
        if (existing is not null)
        {
            throw UserFacingException.Conflict(
                $"A container named {_containerName} already exists ({existing.Status}). Use it from the list of servers, or remove it first: docker rm -f {_containerName}.");
        }

        var hostDataDirectory = await _self.HostDataDirectoryAsync(_docker, cancellationToken);
        var dataFolder = Path.Combine(hostDataDirectory, DataFolderName);
        var backupFolder = string.IsNullOrWhiteSpace(request.BackupFolder)
            ? Path.Combine(hostDataDirectory, BackupFolderName)
            : request.BackupFolder.Trim();

        // A data folder with a cluster in it belongs to an earlier container: the image would
        // start that cluster, with its own passwords, instead of the one being asked for.
        var localData = Path.Combine(_self.LocalDataDirectory, DataFolderName);
        if (Directory.Exists(localData) && Directory.EnumerateFileSystemEntries(localData).Any())
        {
            throw UserFacingException.Conflict(
                $"The data folder {dataFolder} already holds a PostgreSQL cluster, from an earlier container. " +
                "Start that container again (docker start stagezero-postgres) and use it from the list of servers, or move the folder " +
                "aside or delete it, then create again. Nothing was changed.");
        }

        PrepareFolders(hostDataDirectory, dataFolder, backupFolder);

        var network = await _self.NetworkAsync(_docker, cancellationToken);
        var bootstrap = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var spec = new ContainerSpec(
            Image,
            [$"POSTGRES_PASSWORD={bootstrap}", "POSTGRES_USER=postgres", "POSTGRES_DB=postgres"],
            new Dictionary<string, string> { [Label] = "postgres", [$"{Label}.created"] = DateTime.UtcNow.ToString("O") },
            [(dataFolder, "/var/lib/postgresql"), (backupFolder, "/backups")],
            [(5432, request.PublishOnAllInterfaces ? "0.0.0.0" : "127.0.0.1", request.Port)],
            network,
            "unless-stopped");

        // How this app reaches the new server: over the shared Docker network by name (the
        // container's own 5432), or over the published port on a host.
        var (host, port) = network is not null ? (_containerName, 5432) : ("localhost", request.Port);

        _logger.LogWarning("Creating PostgreSQL container {Name} ({Image}) on host port {Port}, data {Data}, backups {Backups}",
            _containerName, Image, request.Port, dataFolder, backupFolder);

        string id;
        try
        {
            await _docker.PullImageAsync(Image, cancellationToken);
            id = await _docker.CreateContainerAsync(_containerName, spec, cancellationToken);
        }
        catch (DockerException ex)
        {
            throw new UserFacingException($"Docker refused: {ex.Message}", ex, StatusCodes.Status400BadRequest);
        }

        try
        {
            await _docker.StartContainerAsync(id, cancellationToken);

            var admin = new NpgsqlConnectionStringBuilder
            {
                Host = host, Port = port, Username = "postgres", Password = bootstrap, Database = "postgres",
                Pooling = false, GssEncryptionMode = GssEncryptionMode.Disable
            }.ConnectionString;
            await _admin.WaitUntilReadyAsync(admin, ReadyTimeout, cancellationToken);

            // The bootstrap password dies here; from now on only the operator knows the superuser's.
            await _admin.ExecuteAsync(admin, $"ALTER USER postgres PASSWORD {Literal(request.AdminPassword)};", cancellationToken);
            admin = new NpgsqlConnectionStringBuilder(admin) { Password = request.AdminPassword }.ConnectionString;
            await _admin.ExecuteAsync(admin, $"CREATE ROLE {RoleName} LOGIN PASSWORD {Literal(request.AppPassword)};", cancellationToken);
            await _admin.ExecuteAsync(admin, $"CREATE DATABASE {DatabaseName} OWNER {RoleName};", cancellationToken);
        }
        catch (Exception ex) when (ex is DockerException or NpgsqlException or TimeoutException or IOException
                                       or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            _logger.LogError(ex, "Setting up {Name} failed; removing the container", _containerName);
            try
            {
                await _docker.RemoveContainerAsync(id, force: true, CancellationToken.None);
            }
            catch (Exception cleanup) when (cleanup is DockerException or HttpRequestException or IOException)
            {
                _logger.LogError(cleanup, "Could not remove {Name} after the failure", _containerName);
            }

            throw new UserFacingException(
                $"The PostgreSQL container started but could not be set up: {ex.Message} The container was removed; " +
                $"the folders {dataFolder} and {backupFolder} were left for you to inspect or delete.",
                ex, StatusCodes.Status400BadRequest);
        }

        _logger.LogWarning("PostgreSQL container {Name} is up: database {Database}, role {Role}", _containerName, DatabaseName, RoleName);
        return new CreatedPostgres(_containerName, host, port, request.Port, DatabaseName, RoleName, dataFolder, backupFolder);
    }

    private static void Validate(CreatePostgresRequest request)
    {
        if (request.Port is < 1024 or > 65535)
        {
            throw new UserFacingException("Choose a port between 1024 and 65535.");
        }

        if (PasswordProblem(request.AdminPassword, "admin") is { } adminProblem)
        {
            throw new UserFacingException(adminProblem);
        }

        if (PasswordProblem(request.AppPassword, "StageZero") is { } appProblem)
        {
            throw new UserFacingException(appProblem);
        }

        if (request.AdminPassword == request.AppPassword)
        {
            throw new UserFacingException("Use two different passwords: the admin one unlocks the whole server, StageZero's only its own database.");
        }

        if (!string.IsNullOrWhiteSpace(request.BackupFolder) && !Path.IsPathRooted(request.BackupFolder.Trim()))
        {
            throw new UserFacingException("The backups folder must be a full path on the machine running Docker.");
        }
    }

    /// <summary>
    /// The folders under the data directory can be created from here (the same directory, seen
    /// through the mount); a backups folder elsewhere is left to Docker, which creates a missing
    /// bind source.
    /// </summary>
    private void PrepareFolders(string hostDataDirectory, string dataFolder, string backupFolder)
    {
        var local = _self.LocalDataDirectory;
        foreach (var folder in new[] { dataFolder, backupFolder })
        {
            if (folder.StartsWith(hostDataDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.Combine(local, Path.GetRelativePath(hostDataDirectory, folder)));
            }
            else if (!_self.InContainer)
            {
                Directory.CreateDirectory(folder);
            }
        }
    }

    /// <summary>A SQL string literal; the passwords are validated to hold no quote, but never trust that alone.</summary>
    private static string Literal(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
