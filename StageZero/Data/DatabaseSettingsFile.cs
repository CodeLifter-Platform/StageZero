using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace StageZero.Data;

/// <summary>
/// The database choice made from the Database page, kept as <c>database.json</c> in the data
/// directory: the provider in clear, the connection string encrypted with the Data Protection
/// key ring (the same one that guards the stored Cloudflare tokens), so a copy of the data
/// directory without its <c>dp-keys/</c> gives no password away.
/// <para>
/// Startup reads it before the host is built, through a provider created directly on the
/// key ring; the API writes it through the host's provider. Same ring, same application
/// name, same purpose, so each side reads what the other wrote. The environment variables
/// still win when set (<see cref="DatabaseSettings.Resolve"/>): the file never overrides a
/// deliberate deployment setting, and removing it is the way back to the SQLite default.
/// </para>
/// </summary>
public sealed class DatabaseSettingsFile
{
    public const string FileName = "database.json";
    public const string ApplicationName = "StageZero";
    private const string Purpose = "StageZero.DatabaseSettings.v1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly IDataProtector _protector;

    public DatabaseSettingsFile(string path, IDataProtectionProvider provider)
    {
        Path = path;
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>Before the host exists: a provider straight on the key ring directory.</summary>
    public static DatabaseSettingsFile Open(string appDataDirectory, DirectoryInfo keyRing) =>
        new(PathIn(appDataDirectory), DataProtectionProvider.Create(keyRing, options => options.SetApplicationName(ApplicationName)));

    public static string PathIn(string appDataDirectory) => System.IO.Path.Combine(appDataDirectory, FileName);

    public string Path { get; }

    public bool Exists => File.Exists(Path);

    /// <summary>
    /// The stored choice, or null when there is none. A file that cannot be read stops
    /// startup with the way out, rather than quietly starting on an empty SQLite database
    /// that would look like a fresh install.
    /// </summary>
    public StoredDatabaseSettings? Read()
    {
        if (!Exists)
        {
            return null;
        }

        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(File.ReadAllText(Path), Json);
        }
        catch (JsonException ex)
        {
            throw Unreadable("is not valid JSON", ex);
        }

        if (document is null || document.Provider != DatabaseProvider.Postgres || string.IsNullOrWhiteSpace(document.Connection))
        {
            throw Unreadable("does not name a PostgreSQL connection", null);
        }

        try
        {
            return new StoredDatabaseSettings(document.Provider, _protector.Unprotect(document.Connection));
        }
        catch (CryptographicException ex)
        {
            throw Unreadable($"cannot be decrypted; the {Services.DataProtectionKeys.DirectoryName} ring it was written with is gone", ex);
        }
    }

    /// <summary>Records a PostgreSQL choice. The connection string is encrypted before it touches the disk.</summary>
    public void Write(string connectionString)
    {
        var document = new Document(DatabaseProvider.Postgres, _protector.Protect(connectionString));
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, Json));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporary, Path, overwrite: true);
    }

    /// <summary>Back to the default: SQLite in the data directory.</summary>
    public void Delete() => File.Delete(Path);

    private InvalidOperationException Unreadable(string problem, Exception? inner) => new(
        $"The database settings file {Path} {problem}. Delete it to start on the SQLite database in the data " +
        $"directory, or set {DatabaseSettings.ProviderVariable} and {DatabaseSettings.ConnectionVariable} to name the database directly.",
        inner);

    private sealed record Document(DatabaseProvider Provider, string Connection);
}

public sealed record StoredDatabaseSettings(DatabaseProvider Provider, string ConnectionString);
