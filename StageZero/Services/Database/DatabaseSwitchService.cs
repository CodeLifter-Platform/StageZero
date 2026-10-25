using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StageZero.Data;
using StageZero.Services.Cli;

namespace StageZero.Services.Database;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Moves StageZero from its SQLite file onto a PostgreSQL database the operator runs
/// elsewhere (the platform Infra stack, or any server), from the web app. One way: once on
/// PostgreSQL, going back is a manual recovery step. The target must be empty, so a switch
/// can never overwrite data; the SQLite file is only read and stays as it was.
/// </summary>
public interface IDatabaseSwitchService
{
    DatabaseStatus GetStatus();

    /// <summary>Connects with the given details and reports what it found. Never writes.</summary>
    Task<PostgresTestResult> TestAsync(PostgresTarget target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies everything into the target, saves it as the database to run on, and restarts the
    /// app onto it. Refused unless the app is on its default SQLite file and the target is
    /// reachable and empty.
    /// </summary>
    Task<DatabaseSwitchResult> SwitchToPostgresAsync(PostgresTarget target, CancellationToken cancellationToken = default);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

/// <summary>A PostgreSQL database as the form describes it. Built into a connection string here, never concatenated.</summary>
public sealed record PostgresTarget(
    string? Host, int? Port, string? Database, string? Username, string? Password, string? SslMode)
{
    public const int DefaultPort = 5432;

    public DatabaseSettings ToSettings()
    {
        var host = Host?.Trim() ?? string.Empty;
        var database = Database?.Trim() ?? string.Empty;
        var username = Username?.Trim() ?? string.Empty;
        if (host.Length == 0) throw new UserFacingException("Host is required.");
        if (database.Length == 0) throw new UserFacingException("Database is required.");
        if (username.Length == 0) throw new UserFacingException("Username is required.");
        if (string.IsNullOrEmpty(Password)) throw new UserFacingException("Password is required.");
        var port = Port ?? DefaultPort;
        if (port is < 1 or > 65535) throw new UserFacingException("Port must be between 1 and 65535.");

        var ssl = string.IsNullOrWhiteSpace(SslMode) ? Npgsql.SslMode.Prefer
            : Enum.TryParse<SslMode>(SslMode.Trim(), ignoreCase: true, out var parsed) ? parsed
            : throw new UserFacingException("SSL mode must be Disable, Prefer or Require.");

        return DatabaseSettings.Postgres(new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = Password,
            SslMode = ssl,
            Timeout = 10
        }.ConnectionString);
    }
}

/// <param name="Description">Where the data is, never the password.</param>
/// <param name="CanSwitch">On the default SQLite file, so a move to PostgreSQL is offered.</param>
public sealed record DatabaseStatus(string Provider, DatabaseSource Source, string Description, bool CanSwitch);

/// <param name="Ok">Connected and can hold StageZero's data.</param>
/// <param name="Problem">Why not, written for the operator; null when ok.</param>
/// <param name="TableCount">Tables already in the database's public schema.</param>
/// <param name="HasData">Any StageZero table already has a row: a switch would be refused.</param>
public sealed record PostgresTestResult(
    bool Ok, string? Problem, string? ServerVersion, int TableCount, bool HasData, IReadOnlyList<string> Warnings);

public sealed record DatabaseSwitchResult(string Description, bool Restarting, IReadOnlyList<string> CopiedTables);

// ═══════════════════════════════════════════════════════════════
// RESTART
// ═══════════════════════════════════════════════════════════════

/// <summary>Ends the process so it starts again on the new database. Swapped out in tests.</summary>
public interface IAppRestarter
{
    void RestartSoon();
}

/// <summary>
/// Stops the host a moment after the response has gone. In Docker, <c>restart: unless-stopped</c>
/// starts the container again; under a service manager, its restart policy does.
/// </summary>
public sealed class HostAppRestarter(IHostApplicationLifetime lifetime, ILogger<HostAppRestarter> logger) : IAppRestarter
{
    public void RestartSoon()
    {
        logger.LogWarning("Restarting to switch databases");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            lifetime.StopApplication();
        });
    }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class DatabaseSwitchService : IDatabaseSwitchService
{
    // One switch at a time: two copies racing into one database would trip over each other.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly DatabaseSelection _current;
    private readonly IDataProtectionProvider _protection;
    private readonly IAppRestarter _restarter;
    private readonly ILogger<DatabaseSwitchService> _logger;

    public DatabaseSwitchService(
        DatabaseSelection current,
        IDataProtectionProvider protection,
        IAppRestarter restarter,
        ILogger<DatabaseSwitchService> logger)
    {
        _current = current;
        _protection = protection;
        _restarter = restarter;
        _logger = logger;
    }

    public DatabaseStatus GetStatus() => new(
        _current.Settings.Provider == DatabaseProvider.Postgres ? "postgres" : "sqlite",
        _current.Source,
        _current.Settings.Describe(),
        CanSwitch);

    private bool CanSwitch => _current.Settings.Provider == DatabaseProvider.Sqlite && _current.Source == DatabaseSource.Default;

    public async Task<PostgresTestResult> TestAsync(PostgresTarget target, CancellationToken cancellationToken = default)
    {
        var settings = target.ToSettings();
        var warnings = new List<string>();
        var parts = new NpgsqlConnectionStringBuilder(settings.ConnectionString);
        var host = parts.Host ?? string.Empty;
        var loopback = host is "localhost" or "127.0.0.1" or "::1";

        try
        {
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var version = (string?)await Scalar(connection, "SHOW server_version", cancellationToken) ?? "unknown";
            var canCreate = (bool?)await Scalar(connection,
                "SELECT has_schema_privilege(current_user, 'public', 'CREATE')", cancellationToken) == true;
            // Email addresses are citext (case-insensitive), created by the first migration
            // that needs it: the server must ship it, and the user must be able to add it.
            var citext = (string?)await Scalar(connection, """
                SELECT CASE
                  WHEN EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'citext') THEN 'installed'
                  WHEN NOT EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'citext') THEN 'missing'
                  WHEN has_database_privilege(current_user, current_database(), 'CREATE') THEN 'creatable'
                  ELSE 'forbidden' END
                """, cancellationToken);
            var tables = await TableNamesAsync(connection, cancellationToken);
            var hasData = await AnyStageZeroRowsAsync(connection, tables, cancellationToken);

            if (int.TryParse(version.Split('.')[0], out var major) && major < 13)
            {
                warnings.Add($"PostgreSQL {version} is old; StageZero is tested on 16 and later.");
            }

            if (!canCreate)
            {
                return new(false, $"Connected, but {parts.Username} can't create tables in the public schema. "
                    + "Make it the database's owner (Infra's create-app-db.sh does).", version, tables.Count, hasData, warnings);
            }

            if (citext is "missing" or "forbidden")
            {
                return new(false, citext == "missing"
                    ? "Connected, but this server doesn't have the citext extension, which StageZero uses so email "
                      + "addresses ignore case. Install PostgreSQL's contrib package (the official images include it)."
                    : $"Connected, but {parts.Username} can't add the citext extension, which StageZero uses so email "
                      + "addresses ignore case. Make it the database's owner (Infra's create-app-db.sh does), or have a "
                      + "superuser run CREATE EXTENSION citext in this database.", version, tables.Count, hasData, warnings);
            }

            if (hasData)
            {
                return new(false, "This database already has StageZero data. Switching only copies into an empty "
                    + "database, so nothing there is ever overwritten. Use a new database.", version, tables.Count, true, warnings);
            }

            if (tables.Count > 0 && !tables.Contains("__EFMigrationsHistory"))
            {
                warnings.Add($"The database already has {tables.Count} other table(s). StageZero's tables will sit beside them; "
                    + "a database of its own is cleaner.");
            }

            return new(true, null, version, tables.Count, false, warnings);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            var problem = ex is PostgresException pg ? pg.MessageText : ex.Message;
            if (loopback && DataPathService.IsRunningInDocker())
            {
                problem += " StageZero runs in a container, where localhost is the container itself. For a server on "
                    + "this machine (such as the Infra stack) use host.docker.internal.";
            }

            _logger.LogInformation(ex, "PostgreSQL connection test to {Host} failed", host);
            return new(false, $"Couldn't connect: {problem}", null, 0, false, warnings);
        }
    }

    public async Task<DatabaseSwitchResult> SwitchToPostgresAsync(PostgresTarget target, CancellationToken cancellationToken = default)
    {
        if (!CanSwitch)
        {
            throw UserFacingException.Conflict(_current.Source == DatabaseSource.Environment
                ? $"The database is set by {DatabaseSettings.ProviderVariable} / {DatabaseSettings.ConnectionVariable}; "
                  + "change it there."
                : "StageZero is already on PostgreSQL. Switching runs one way.");
        }

        if (!await Gate.WaitAsync(0, cancellationToken))
        {
            throw UserFacingException.Conflict("A database switch is already running.");
        }

        try
        {
            var test = await TestAsync(target, cancellationToken);
            if (!test.Ok) throw new UserFacingException(test.Problem ?? "The database isn't usable.");

            var destination = target.ToSettings();
            _logger.LogWarning("Switching databases: copying {From} to {To}", _current.Settings.Describe(), destination.Describe());

            using var output = new StringWriter();
            var outcome = await CopyToPostgresCommand.CopyAsync(_current.Settings, destination, output, cancellationToken);
            if (outcome != 0)
            {
                throw UserFacingException.Conflict("The database got data while the switch ran; nothing was changed. Use an empty database.");
            }

            // Saved only after the copy committed: a failure above leaves the app on SQLite.
            DatabaseChoice.Save(DataPathService.GetAppDataDirectory(), _protection, destination);
            _logger.LogWarning("Switched to {Database}. The SQLite file {Path} is kept as it was",
                destination.Describe(), _current.Settings.SqlitePath);

            _restarter.RestartSoon();
            var copied = output.ToString()
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains(" row(s)", StringComparison.Ordinal))
                .ToList();
            return new DatabaseSwitchResult(destination.Describe(), true, copied);
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogError(ex, "The database switch failed; StageZero stays on {Database}", _current.Settings.Describe());
            throw new UserFacingException($"The switch failed and StageZero stays on SQLite: {ex.Message}", ex);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> TableNamesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken)) names.Add(reader.GetString(0));
        return names;
    }

    /// <summary>Any row in a table StageZero owns. Its table names come from the model, never from input.</summary>
    private static async Task<bool> AnyStageZeroRowsAsync(
        NpgsqlConnection connection, HashSet<string> tables, CancellationToken cancellationToken)
    {
        await using var model = DatabaseSettings.Postgres("Host=unused").CreateContext();
        foreach (var entity in model.Model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            var name = entity.GetTableName();
            if (name is null || !tables.Contains(name)) continue;

            var quoted = "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            if ((bool?)await Scalar(connection, $"SELECT EXISTS (SELECT 1 FROM {quoted})", cancellationToken) == true)
            {
                return true;
            }
        }

        return false;
    }
}
