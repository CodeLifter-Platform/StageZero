using StageZero.Data;

namespace StageZero.Tests.Data;

public class DatabaseSettingsTests
{
    private const string SqlitePath = "/app-data/stagezero.db";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sqlite")]
    [InlineData(" SQLite ")]
    public void SQLite_in_the_data_directory_is_the_default(string? provider)
    {
        var settings = DatabaseSettings.From(provider, null, () => SqlitePath);

        Assert.Equal(DatabaseProvider.Sqlite, settings.Provider);
        Assert.Equal(SqlitePath, settings.SqlitePath);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("PostgreSQL")]
    public void Postgres_takes_its_connection_string(string provider)
    {
        var settings = DatabaseSettings.From(provider, "Host=db;Database=stagezero;Username=sz;Password=pw", () => SqlitePath);

        Assert.Equal(DatabaseProvider.Postgres, settings.Provider);
        Assert.Null(settings.SqlitePath);
    }

    [Fact]
    public void Postgres_without_a_connection_string_stops_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DatabaseSettings.From("postgres", " ", () => SqlitePath));

        Assert.Contains(DatabaseSettings.ConnectionVariable, error.Message);
    }

    [Fact]
    public void A_misspelt_provider_stops_startup_rather_than_falling_back_to_SQLite()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DatabaseSettings.From("postgress", null, () => SqlitePath));

        Assert.Contains("postgress", error.Message);
    }

    [Fact]
    public void The_logged_description_never_carries_the_password()
    {
        var settings = DatabaseSettings.Postgres("Host=db;Port=5432;Database=stagezero;Username=sz;Password=hunter2-secret");

        Assert.DoesNotContain("hunter2-secret", settings.Describe());
        Assert.Contains("stagezero", settings.Describe());
    }
}
