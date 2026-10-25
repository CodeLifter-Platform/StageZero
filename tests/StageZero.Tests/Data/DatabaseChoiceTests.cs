using Microsoft.AspNetCore.DataProtection;
using StageZero.Data;

namespace StageZero.Tests.Data;

/// <summary>Which database startup picks: the variables, else the web app's saved choice, else SQLite.</summary>
[Collection(Infrastructure.AppCollection.Name)] // touches process-wide environment variables
public sealed class DatabaseChoiceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("stagezero-choice-").FullName;
    private readonly IDataProtectionProvider _protection;
    private const string Saved = "Host=infra;Port=5432;Database=stagezero;Username=stagezero;Password=s3cret-value";

    public DatabaseChoiceTests()
    {
        _protection = DatabaseChoice.KeyRingProvider(_directory);
        Environment.SetEnvironmentVariable(DatabaseSettings.ProviderVariable, null);
        Environment.SetEnvironmentVariable(DatabaseSettings.ConnectionVariable, null);
    }

    private string Sqlite => Path.Combine(_directory, "stagezero.db");

    [Fact]
    public void Nothing_chosen_is_the_sqlite_file()
    {
        var selection = DatabaseChoice.Resolve(_directory, _protection, Sqlite);

        Assert.Equal((DatabaseProvider.Sqlite, DatabaseSource.Default, Sqlite),
            (selection.Settings.Provider, selection.Source, selection.Settings.SqlitePath));
    }

    [Fact]
    public void A_saved_choice_is_read_back_and_its_password_is_not_in_the_file()
    {
        DatabaseChoice.Save(_directory, _protection, DatabaseSettings.Postgres(Saved));

        var selection = DatabaseChoice.Resolve(_directory, _protection, Sqlite);

        Assert.Equal((DatabaseProvider.Postgres, DatabaseSource.Saved), (selection.Settings.Provider, selection.Source));
        Assert.Contains("s3cret-value", selection.Settings.ConnectionString);
        Assert.DoesNotContain("s3cret-value", File.ReadAllText(DatabaseChoice.FilePath(_directory)));
    }

    [Fact]
    public void The_environment_wins_over_a_saved_choice()
    {
        DatabaseChoice.Save(_directory, _protection, DatabaseSettings.Postgres(Saved));
        Environment.SetEnvironmentVariable(DatabaseSettings.ProviderVariable, "sqlite");
        try
        {
            var selection = DatabaseChoice.Resolve(_directory, _protection, Sqlite);

            Assert.Equal((DatabaseProvider.Sqlite, DatabaseSource.Environment), (selection.Settings.Provider, selection.Source));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabaseSettings.ProviderVariable, null);
        }
    }

    [Fact]
    public void A_saved_choice_that_cannot_be_decrypted_stops_startup_instead_of_falling_back_to_sqlite()
    {
        DatabaseChoice.Save(_directory, _protection, DatabaseSettings.Postgres(Saved));
        Directory.Delete(Path.Combine(_directory, "dp-keys"), recursive: true); // the key ring is lost
        var fresh = DatabaseChoice.KeyRingProvider(_directory);

        var error = Assert.Throws<InvalidOperationException>(() => DatabaseChoice.Resolve(_directory, fresh, Sqlite));

        Assert.Contains("can't be decrypted", error.Message);
        Assert.Contains(DatabaseChoice.FileName, error.Message);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }
}
