using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StageZero.Services;

namespace StageZero.Data;

public enum DatabaseProvider
{
    Sqlite,
    Postgres
}

/// <summary>Where the running database choice came from; the Database page shows it and acts on it.</summary>
public enum DatabaseSettingsSource
{
    /// <summary>Nothing chosen: SQLite in the data directory.</summary>
    Default,

    /// <summary>The Database page's choice, kept in <see cref="DatabaseSettingsFile"/>.</summary>
    Settings,

    /// <summary>The environment variables, which the Database page cannot change.</summary>
    Environment
}

/// <summary>
/// Which database StageZero keeps its data in. SQLite, one file in the data directory, unless
/// <c>STAGEZERO_DB_PROVIDER=postgres</c> and <c>STAGEZERO_DB_CONNECTION</c> name a PostgreSQL
/// database, or the Database page chose one (<see cref="DatabaseSettingsFile"/>). Either way
/// the data directory still holds the logs and the Data Protection keys that decrypt the
/// stored Cloudflare tokens.
/// </summary>
public sealed record DatabaseSettings(DatabaseProvider Provider, string ConnectionString)
{
    public const string ProviderVariable = "STAGEZERO_DB_PROVIDER";
    public const string ConnectionVariable = "STAGEZERO_DB_CONNECTION";

    public DatabaseSettingsSource Source { get; init; } = DatabaseSettingsSource.Default;

    public static DatabaseSettings Sqlite(string databasePath) =>
        new(DatabaseProvider.Sqlite, new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());

    public static DatabaseSettings Postgres(string connectionString)
    {
        // Npgsql tries Kerberos (GSS) encryption first by default, which on the slim runtime
        // image logs a missing libgssapi on every connection. Off unless asked for.
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!connectionString.Contains("gss", StringComparison.OrdinalIgnoreCase))
        {
            builder.GssEncryptionMode = GssEncryptionMode.Disable;
        }

        return new DatabaseSettings(DatabaseProvider.Postgres, builder.ConnectionString);
    }

    /// <summary>
    /// What the app (and the CLI commands that must find the same database) runs on: the
    /// environment variables when either is set, else the Database page's stored choice, else
    /// SQLite in the data directory.
    /// </summary>
    public static DatabaseSettings Resolve(DatabaseSettingsFile stored) =>
        Resolve(Environment.GetEnvironmentVariable(ProviderVariable),
                Environment.GetEnvironmentVariable(ConnectionVariable),
                stored,
                DataPathService.GetDatabasePath);

    /// <summary>
    /// A misspelt provider, or postgres without a connection string, stops startup rather than
    /// quietly falling back to a SQLite file nobody is backing up (<see cref="From"/>).
    /// </summary>
    public static DatabaseSettings Resolve(string? provider, string? connection, DatabaseSettingsFile stored, Func<string> sqlitePath)
    {
        if (!string.IsNullOrWhiteSpace(provider) || !string.IsNullOrWhiteSpace(connection))
        {
            return From(provider, connection, sqlitePath) with { Source = DatabaseSettingsSource.Environment };
        }

        if (stored.Read() is { } choice)
        {
            return Postgres(choice.ConnectionString) with { Source = DatabaseSettingsSource.Settings };
        }

        return Sqlite(sqlitePath());
    }

    public static DatabaseSettings From(string? provider, string? connection, Func<string> sqlitePath)
    {
        switch (provider?.Trim().ToLowerInvariant())
        {
            case null or "" or "sqlite":
                return string.IsNullOrWhiteSpace(connection)
                    ? Sqlite(sqlitePath())
                    : new DatabaseSettings(DatabaseProvider.Sqlite, connection);

            case "postgres" or "postgresql" or "npgsql":
                if (string.IsNullOrWhiteSpace(connection))
                {
                    throw new InvalidOperationException(
                        $"{ProviderVariable}=postgres needs {ConnectionVariable}, a PostgreSQL connection string " +
                        "such as Host=db;Database=stagezero;Username=stagezero;Password=...");
                }

                return Postgres(connection);

            default:
                throw new InvalidOperationException(
                    $"{ProviderVariable} is '{provider}'; expected 'sqlite' (the default) or 'postgres'.");
        }
    }

    /// <summary>The SQLite file, or null on PostgreSQL.</summary>
    public string? SqlitePath => Provider == DatabaseProvider.Sqlite
        ? new SqliteConnectionStringBuilder(ConnectionString).DataSource
        : null;

    /// <summary>Where the data is, for the log: never the password.</summary>
    public string Describe()
    {
        if (Provider == DatabaseProvider.Sqlite)
        {
            return $"SQLite {SqlitePath}";
        }

        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        return $"PostgreSQL {builder.Database} on {builder.Host}:{builder.Port} as {builder.Username}";
    }

    /// <summary>
    /// Registers <c>IDbContextFactory&lt;ApplicationDbContext&gt;</c>, which on PostgreSQL hands
    /// out <see cref="PostgresApplicationDbContext"/> so its own migrations apply.
    /// </summary>
    public void AddDbContextFactory(IServiceCollection services)
    {
        if (Provider == DatabaseProvider.Sqlite)
        {
            services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlite(ConnectionString));
            return;
        }

        services.AddDbContextFactory<PostgresApplicationDbContext>(options => options.UseNpgsql(ConnectionString));
        services.AddSingleton<IDbContextFactory<ApplicationDbContext>>(sp =>
            new UpcastFactory(sp.GetRequiredService<IDbContextFactory<PostgresApplicationDbContext>>()));
    }

    /// <summary>A context outside the container (startup, the CLI), unpooled so no handle outlives it.</summary>
    public ApplicationDbContext CreateContext() => Provider switch
    {
        DatabaseProvider.Sqlite => DatabaseInitializer.CreateContext(SqlitePath!),
        _ => new PostgresApplicationDbContext(new DbContextOptionsBuilder<PostgresApplicationDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ToString())
            .Options)
    };

    private sealed class UpcastFactory(IDbContextFactory<PostgresApplicationDbContext> inner)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => inner.CreateDbContext();
    }
}
