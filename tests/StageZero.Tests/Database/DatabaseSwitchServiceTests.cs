using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Data;
using StageZero.Services;
using StageZero.Services.Database;
using StageZero.Tests.Fakes;

namespace StageZero.Tests.Database;

/// <summary>The Database page's rules, without a host: what may change, and what is refused.</summary>
public sealed class DatabaseSwitchServiceTests : IDisposable
{
    private readonly string _appData = Directory.CreateTempSubdirectory("stagezero-switch-").FullName;
    private readonly FakeAppRestart _restart = new();

    private DatabaseSwitchService Service(DatabaseSettings current) =>
        new(current, DatabaseSettingsFile.Open(_appData, DataProtectionKeys.Prepare(_appData)), _restart, NullLogger<DatabaseSwitchService>.Instance);

    private DatabaseSettings SqliteDefault() => DatabaseSettings.Sqlite(Path.Combine(_appData, "stagezero.db"));

    [Fact]
    public void A_database_set_by_the_environment_cannot_be_changed_from_the_page()
    {
        var service = Service(DatabaseSettings.Postgres("Host=db;Database=sz;Username=u;Password=p") with { Source = DatabaseSettingsSource.Environment });

        Assert.False(service.Status().CanChange);
        var error = Assert.Throws<UserFacingException>(() => service.RevertToSqlite());
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.Contains(DatabaseSettings.ProviderVariable, error.Message);
        Assert.Empty(_restart.Reasons);
    }

    [Fact]
    public async Task A_switch_refused_by_the_environment_never_touches_the_target()
    {
        var service = Service(SqliteDefault() with { Source = DatabaseSettingsSource.Environment });

        var error = await Assert.ThrowsAsync<UserFacingException>(() =>
            service.SwitchToPostgresAsync("Host=127.0.0.1;Port=1;Database=sz;Username=u;Password=p", copyData: true));

        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.False(File.Exists(DatabaseSettingsFile.PathIn(_appData)));
    }

    [Fact]
    public void The_default_install_reports_SQLite_and_may_switch()
    {
        var status = Service(SqliteDefault()).Status();

        Assert.Equal((DatabaseProvider.Sqlite, DatabaseSettingsSource.Default, true, false), (status.Provider, status.Source, status.CanChange, status.RestartPending));
        Assert.StartsWith("SQLite", status.Description);
    }

    [Fact]
    public void Going_back_to_SQLite_from_the_default_is_a_conflict()
    {
        var error = Assert.Throws<UserFacingException>(() => Service(SqliteDefault()).RevertToSqlite());

        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
    }

    [Fact]
    public void Going_back_to_SQLite_forgets_the_stored_choice_and_restarts()
    {
        var file = DatabaseSettingsFile.Open(_appData, DataProtectionKeys.Prepare(_appData));
        file.Write("Host=db;Database=sz;Username=u;Password=p");
        var service = Service(DatabaseSettings.Postgres("Host=db;Database=sz;Username=u;Password=p") with { Source = DatabaseSettingsSource.Settings });

        var result = service.RevertToSqlite();

        Assert.False(file.Exists);
        Assert.Single(_restart.Reasons);
        Assert.Contains("stays in PostgreSQL", result.Message);
        Assert.False(service.Status().CanChange); // a restart is pending
    }

    [Theory]
    [InlineData("", "Enter the PostgreSQL connection string")]
    [InlineData("this is not a connection string", "isn't a PostgreSQL connection string")]
    [InlineData("Database=sz;Username=u;Password=p", "needs a Host")]
    [InlineData("Host=db;Username=u;Password=p", "needs a Database")]
    public async Task An_unusable_connection_string_is_refused_with_the_reason(string connection, string reason)
    {
        var error = await Assert.ThrowsAsync<UserFacingException>(() => Service(SqliteDefault()).TestAsync(connection));

        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_is_reported_without_the_password()
    {
        var error = await Assert.ThrowsAsync<UserFacingException>(() =>
            Service(SqliteDefault()).TestAsync("Host=127.0.0.1;Port=1;Database=sz;Username=u;Password=hunter2-secret;Timeout=2"));

        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
        Assert.Contains("Could not connect to PostgreSQL on 127.0.0.1:1", error.Message);
        Assert.DoesNotContain("hunter2", error.Message);
    }

    public void Dispose() => Directory.Delete(_appData, recursive: true);
}
