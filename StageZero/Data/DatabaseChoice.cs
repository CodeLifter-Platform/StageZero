using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using StageZero.Services;

namespace StageZero.Data;

/// <summary>Where the database StageZero runs on was chosen.</summary>
public enum DatabaseSource
{
    /// <summary>Nothing chosen: SQLite in the data directory.</summary>
    Default,

    /// <summary>Chosen in the web app (Settings, or the first-run page): <c>database.json</c>.</summary>
    Saved,

    /// <summary><c>STAGEZERO_DB_PROVIDER</c> / <c>STAGEZERO_DB_CONNECTION</c>, which win over a saved choice.</summary>
    Environment
}

/// <summary>The database this process runs on, and where that came from.</summary>
public sealed record DatabaseSelection(DatabaseSettings Settings, DatabaseSource Source);

/// <summary>
/// The database chosen in the web app, kept in <c>database.json</c> in the data directory
/// rather than in the database itself, because startup needs it before any database is open.
/// The connection string carries a password, so it is encrypted with the same Data
/// Protection key ring as the Cloudflare token. Only PostgreSQL is ever saved: SQLite is what
/// no file means, and the switch runs one way.
/// </summary>
public static class DatabaseChoice
{
    public const string FileName = "database.json";
    private const string Purpose = "StageZero.DatabaseConnection.v1";

    private sealed record Stored(string Provider, string ProtectedConnection, DateTime SavedAt);

    public static string FilePath(string appDataDirectory) => Path.Combine(appDataDirectory, FileName);

    /// <summary>
    /// The database to run on: the environment variables if either is set, else a saved
    /// choice, else SQLite in the data directory. A saved choice that can't be read stops
    /// startup: falling back to the SQLite file would quietly serve stale data.
    /// </summary>
    public static DatabaseSelection Resolve(string appDataDirectory, IDataProtectionProvider protection, string sqlitePath)
    {
        if (!string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable(DatabaseSettings.ProviderVariable))
            || !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable(DatabaseSettings.ConnectionVariable)))
        {
            return new DatabaseSelection(DatabaseSettings.FromEnvironment(), DatabaseSource.Environment);
        }

        var saved = Read(appDataDirectory, protection);
        return saved is null
            ? new DatabaseSelection(DatabaseSettings.Sqlite(sqlitePath), DatabaseSource.Default)
            : new DatabaseSelection(saved, DatabaseSource.Saved);
    }

    /// <summary><see cref="Resolve(string, IDataProtectionProvider, string)"/> for the CLI and startup, with the app's own key ring.</summary>
    public static DatabaseSelection Resolve()
    {
        var dataDirectory = DataPathService.GetAppDataDirectory();
        return Resolve(dataDirectory, KeyRingProvider(dataDirectory), DataPathService.GetDatabasePath());
    }

    /// <summary>The same key ring and application name Program.cs gives Data Protection.</summary>
    public static IDataProtectionProvider KeyRingProvider(string appDataDirectory) =>
        DataProtectionProvider.Create(
            DataProtectionKeys.Prepare(appDataDirectory),
            builder => builder.SetApplicationName("StageZero"));

    public static DatabaseSettings? Read(string appDataDirectory, IDataProtectionProvider protection)
    {
        var path = FilePath(appDataDirectory);
        if (!File.Exists(path)) return null;

        Stored? stored;
        try
        {
            stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{path} is not readable ({ex.Message}). {Recovery(path)}", ex);
        }

        if (stored is null || !string.Equals(stored.Provider, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{path} names no PostgreSQL database. {Recovery(path)}");
        }

        string connection;
        try
        {
            connection = protection.CreateProtector(Purpose).Unprotect(stored.ProtectedConnection);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The saved PostgreSQL connection in {path} can't be decrypted: the data-protection keys it was "
                + $"saved with (dp-keys/) are gone. {Recovery(path)}", ex);
        }

        return DatabaseSettings.Postgres(connection);
    }

    public static void Save(string appDataDirectory, IDataProtectionProvider protection, DatabaseSettings settings)
    {
        if (settings.Provider != DatabaseProvider.Postgres)
        {
            throw new ArgumentException("Only a PostgreSQL choice is saved; SQLite is the default.", nameof(settings));
        }

        var stored = new Stored("postgres", protection.CreateProtector(Purpose).Protect(settings.ConnectionString), DateTime.UtcNow);
        var path = FilePath(appDataDirectory);

        // Write beside, then move over: a crash mid-write must not leave half a file that
        // stops the next startup.
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private static string Recovery(string path) =>
        $"Set {DatabaseSettings.ProviderVariable} and {DatabaseSettings.ConnectionVariable} to the PostgreSQL database, "
        + $"or delete {path} to start on the SQLite file it replaced (that file holds the data as it was before the switch).";
}
