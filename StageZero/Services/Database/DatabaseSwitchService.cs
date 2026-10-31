using System.Net.Sockets;
using Npgsql;
using StageZero.Data;
using StageZero.Services.Cli;

namespace StageZero.Services.Database;

/// <summary>
/// The Database page: where the data is now, whether a PostgreSQL server can be reached, and
/// the move from SQLite to PostgreSQL (or back) made from the browser. The choice is kept in
/// <see cref="DatabaseSettingsFile"/>, encrypted, and takes effect at the restart this asks
/// for; the environment variables, when set, are not something the page can override.
/// </summary>
public interface IDatabaseSwitchService
{
    DatabaseStatus Status();

    /// <summary>Opens a connection and looks at what is there. Nothing is written.</summary>
    Task<ConnectionTest> TestAsync(string connectionString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the target, copies the SQLite data into it when asked (the target must be empty),
    /// records the choice, and schedules the restart that makes it current.
    /// </summary>
    Task<SwitchResult> SwitchToPostgresAsync(string connectionString, bool copyData, CancellationToken cancellationToken = default);

    /// <summary>Forgets the stored choice; the SQLite file is where it was left.</summary>
    SwitchResult RevertToSqlite();
}

public sealed record DatabaseStatus(
    DatabaseProvider Provider,
    /// <summary>Where the data is, never the password (<see cref="DatabaseSettings.Describe"/>).</summary>
    string Description,
    DatabaseSettingsSource Source,
    /// <summary>The page may switch: the choice isn't the environment's, and no restart is under way.</summary>
    bool CanChange,
    bool RestartPending,
    /// <summary>A SQLite database is in the data directory, so a switch can copy it.</summary>
    bool SqliteFileExists,
    /// <summary>Running in a container, which the compose files restart on their own.</summary>
    bool RestartsAutomatically);

public sealed record ConnectionTest(
    string Host,
    int Port,
    string Database,
    string Username,
    string ServerVersion,
    /// <summary>The named database is on the server. When not, the switch creates it (the account needs CREATEDB).</summary>
    bool DatabaseExists,
    /// <summary>No tables at all in the connection's schema.</summary>
    bool IsEmpty,
    /// <summary>StageZero's migration history is there: a database it has used before.</summary>
    bool HasStageZeroSchema);

public sealed record SwitchResult(string Message, IReadOnlyList<CopyToPostgresCommand.CopiedTable> Copied);

public sealed class DatabaseSwitchService : IDatabaseSwitchService
{
    /// <summary>A test should answer while the person is still looking at the button.</summary>
    private const int MaxConnectTimeoutSeconds = 10;

    private readonly DatabaseSettings _current;
    private readonly DatabaseSettingsFile _stored;
    private readonly IAppRestart _restart;
    private readonly ILogger<DatabaseSwitchService> _logger;

    public DatabaseSwitchService(
        DatabaseSettings current, DatabaseSettingsFile stored, IAppRestart restart, ILogger<DatabaseSwitchService> logger)
    {
        _current = current;
        _stored = stored;
        _restart = restart;
        _logger = logger;
    }

    private string SqlitePath => _current.SqlitePath ?? DataPathService.GetDatabasePath();

    public DatabaseStatus Status() => new(
        _current.Provider,
        _current.Describe(),
        _current.Source,
        CanChange: _current.Source != DatabaseSettingsSource.Environment && !_restart.Pending,
        RestartPending: _restart.Pending,
        SqliteFileExists: File.Exists(SqlitePath),
        RestartsAutomatically: DataPathService.IsRunningInContainer());

    public async Task<ConnectionTest> TestAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var builder = Parse(connectionString);
        builder.Timeout = Math.Min(builder.Timeout, MaxConnectTimeoutSeconds);
        builder.Pooling = false;

        try
        {
            try
            {
                await using var connection = new NpgsqlConnection(builder.ConnectionString);
                await connection.OpenAsync(cancellationToken);

                await using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT count(*)::int, " +
                    "bool_or(table_name = '__EFMigrationsHistory') " +
                    "FROM information_schema.tables WHERE table_schema = current_schema()";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                var tables = reader.GetInt32(0);
                var hasHistory = !reader.IsDBNull(1) && reader.GetBoolean(1);

                return new ConnectionTest(
                    connection.Host ?? builder.Host ?? string.Empty,
                    connection.Port,
                    connection.Database,
                    connection.UserName ?? builder.Username ?? string.Empty,
                    connection.PostgreSqlVersion.ToString(),
                    DatabaseExists: true,
                    IsEmpty: tables == 0,
                    HasStageZeroSchema: hasHistory);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
            {
                // The server and the account are fine; the database isn't there yet. The
                // switch creates it, as the migrations do, so report the server as reached.
                var maintenance = new NpgsqlConnectionStringBuilder(builder.ConnectionString) { Database = "postgres" };
                await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
                await connection.OpenAsync(cancellationToken);

                return new ConnectionTest(
                    connection.Host ?? builder.Host ?? string.Empty,
                    connection.Port,
                    builder.Database!,
                    connection.UserName ?? builder.Username ?? string.Empty,
                    connection.PostgreSqlVersion.ToString(),
                    DatabaseExists: false,
                    IsEmpty: true,
                    HasStageZeroSchema: false);
            }
        }
        catch (Exception ex) when (ex is NpgsqlException or SocketException or TimeoutException or IOException)
        {
            // The driver's message is written for a person ("password authentication failed
            // for user ...", "Connection refused"); it never carries the password.
            throw new UserFacingException(
                $"Could not connect to PostgreSQL on {builder.Host}:{builder.Port}: {Innermost(ex).Message}",
                ex, StatusCodes.Status400BadRequest);
        }
    }

    public async Task<SwitchResult> SwitchToPostgresAsync(string connectionString, bool copyData, CancellationToken cancellationToken = default)
    {
        RequireChangeable();
        var test = await TestAsync(connectionString, cancellationToken);
        var target = DatabaseSettings.Postgres(connectionString);

        if (copyData && _current.Provider != DatabaseProvider.Sqlite)
        {
            throw new UserFacingException(
                "There is no SQLite data to copy: StageZero is already on PostgreSQL. Switch without copying to point it at the new database.");
        }

        var copied = new List<CopyToPostgresCommand.CopiedTable>();
        try
        {
            if (copyData)
            {
                _logger.LogWarning("Copying {Source} to {Target}", _current.Describe(), target.Describe());
                copied = await CopyToPostgresCommand.CopyAsync(_current.SqlitePath!, connectionString, cancellationToken);
            }
            else
            {
                // Bring the target to the current schema now, so a database that can't take it
                // is found while there is still a running app to say so.
                await DatabaseInitializer.InitializeAsync(target, _logger, cancellationToken);
            }
        }
        catch (NpgsqlException ex)
        {
            // Creating the database without CREATEDB, a schema the account may not write, a
            // connection lost half way: the server's words, with nothing changed on our side.
            throw new UserFacingException(
                $"PostgreSQL refused while preparing {test.Database} on {test.Host}: {Innermost(ex).Message}. Nothing was changed.",
                ex, StatusCodes.Status400BadRequest);
        }

        _stored.Write(connectionString);
        _logger.LogWarning("Database choice recorded in {File}: {Target}", _stored.Path, target.Describe());
        _restart.Request($"switch to {target.Describe()}");

        var rows = copied.Sum(c => c.Rows);
        var message = copyData
            ? $"Copied {rows} row(s) to PostgreSQL {test.Database} on {test.Host}. StageZero is restarting on it now; the SQLite file stays where it was, unchanged."
            : $"StageZero is restarting on PostgreSQL {test.Database} on {test.Host}.";
        return new SwitchResult(message, copied);
    }

    public SwitchResult RevertToSqlite()
    {
        RequireChangeable();
        if (_current.Source != DatabaseSettingsSource.Settings)
        {
            throw UserFacingException.Conflict("StageZero is already on the SQLite database in the data directory.");
        }

        _stored.Delete();
        _logger.LogWarning("Database choice removed ({File}); restarting on SQLite {Path}", _stored.Path, SqlitePath);
        _restart.Request($"go back to SQLite {SqlitePath}");

        return new SwitchResult(
            $"StageZero is restarting on the SQLite database at {SqlitePath}, as it was when you switched. " +
            "Anything changed since then stays in PostgreSQL.", []);
    }

    private void RequireChangeable()
    {
        if (_current.Source == DatabaseSettingsSource.Environment)
        {
            throw UserFacingException.Conflict(
                $"The database is set by {DatabaseSettings.ProviderVariable} and {DatabaseSettings.ConnectionVariable} in the " +
                "environment. Change those (in .env or the compose file) and restart StageZero instead.");
        }

        if (_restart.Pending)
        {
            throw UserFacingException.Conflict("StageZero is already restarting.");
        }
    }

    private static NpgsqlConnectionStringBuilder Parse(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new UserFacingException("Enter the PostgreSQL connection string.");
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(DatabaseSettings.Postgres(connectionString).ConnectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw new UserFacingException(
                $"That isn't a PostgreSQL connection string ({ex.Message}). " +
                "Expected the form Host=db;Port=5432;Database=stagezero;Username=stagezero;Password=...");
        }

        if (string.IsNullOrWhiteSpace(builder.Host))
        {
            throw new UserFacingException("The connection string needs a Host.");
        }

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new UserFacingException("The connection string needs a Database.");
        }

        return builder;
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is not null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }
}
