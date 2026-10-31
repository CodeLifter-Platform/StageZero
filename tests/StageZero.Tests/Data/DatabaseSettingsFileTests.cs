using StageZero.Data;
using StageZero.Services;

namespace StageZero.Tests.Data;

/// <summary>
/// The Database page's stored choice: encrypted on disk, read back at startup, outranked by
/// the environment, and never the reason a misread install quietly starts on an empty SQLite
/// database.
/// </summary>
public sealed class DatabaseSettingsFileTests : IDisposable
{
    private const string Connection = "Host=db;Database=stagezero;Username=stagezero;Password=hunter2-very-secret";
    private readonly string _appData = Directory.CreateTempSubdirectory("stagezero-dbsettings-").FullName;

    private DatabaseSettingsFile Open(string? ring = null) =>
        DatabaseSettingsFile.Open(_appData, DataProtectionKeys.Prepare(ring ?? _appData));

    private string Sqlite() => Path.Combine(_appData, "stagezero.db");

    [Fact]
    public void A_stored_choice_round_trips_and_the_password_never_reaches_the_disk_in_clear()
    {
        Open().Write(Connection);

        var stored = Open().Read();

        Assert.Equal(Connection, stored!.ConnectionString);
        Assert.Equal(DatabaseProvider.Postgres, stored.Provider);
        Assert.DoesNotContain("hunter2", File.ReadAllText(DatabaseSettingsFile.PathIn(_appData)));
    }

    [Fact]
    public void Nothing_stored_means_the_SQLite_default()
    {
        var settings = DatabaseSettings.Resolve(null, null, Open(), Sqlite);

        Assert.Equal(DatabaseProvider.Sqlite, settings.Provider);
        Assert.Equal(DatabaseSettingsSource.Default, settings.Source);
        Assert.Equal(Sqlite(), settings.SqlitePath);
    }

    [Fact]
    public void A_stored_choice_is_used_when_the_environment_says_nothing()
    {
        Open().Write(Connection);

        var settings = DatabaseSettings.Resolve(null, "", Open(), Sqlite);

        Assert.Equal(DatabaseProvider.Postgres, settings.Provider);
        Assert.Equal(DatabaseSettingsSource.Settings, settings.Source);
        Assert.Contains("Database=stagezero", settings.ConnectionString);
    }

    [Fact]
    public void The_environment_outranks_a_stored_choice()
    {
        Open().Write(Connection);

        var settings = DatabaseSettings.Resolve("sqlite", null, Open(), Sqlite);

        Assert.Equal(DatabaseProvider.Sqlite, settings.Provider);
        Assert.Equal(DatabaseSettingsSource.Environment, settings.Source);
    }

    [Fact]
    public void Deleting_the_file_goes_back_to_the_default()
    {
        var file = Open();
        file.Write(Connection);

        file.Delete();

        Assert.False(file.Exists);
        Assert.Null(file.Read());
    }

    [Fact]
    public void A_file_that_is_not_JSON_stops_startup_and_names_the_way_out()
    {
        File.WriteAllText(DatabaseSettingsFile.PathIn(_appData), "{ not json");

        var error = Assert.Throws<InvalidOperationException>(() => DatabaseSettings.Resolve(null, null, Open(), Sqlite));

        Assert.Contains(DatabaseSettingsFile.FileName, error.Message);
        Assert.Contains("Delete it", error.Message);
        Assert.Contains(DatabaseSettings.ProviderVariable, error.Message);
    }

    [Fact]
    public void A_file_written_under_a_lost_key_ring_stops_startup_rather_than_starting_empty()
    {
        Open().Write(Connection);
        var otherRing = Directory.CreateTempSubdirectory("stagezero-other-ring-").FullName;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => Open(otherRing).Read());

            Assert.Contains(DataProtectionKeys.DirectoryName, error.Message);
            Assert.Contains("Delete it", error.Message);
        }
        finally
        {
            Directory.Delete(otherRing, recursive: true);
        }
    }

    public void Dispose() => Directory.Delete(_appData, recursive: true);
}
