using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StageZero.Data;
using StageZero.Services.Database;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Api;

/// <summary>
/// Choosing the database in the web app: test a PostgreSQL database, copy into it and switch
/// (one way, empty targets only), from Settings or, before any user exists, the first-run page.
/// </summary>
[Collection(AppCollection.Name)]
public class DatabaseApiTests
{
    private sealed class RecordingRestarter : IAppRestarter
    {
        public int Restarts { get; private set; }
        public void RestartSoon() => Restarts++;
    }

    private readonly RecordingRestarter _restarter = new();

    private StageZeroApp CreateApp() => new(sqlite: true)
    {
        ConfigureServices = services => services.AddSingleton<IAppRestarter>(_restarter)
    };

    /// <summary>A fresh, empty database on the test server, as Infra's create-app-db.sh leaves one.</summary>
    private static async Task<NpgsqlConnectionStringBuilder> CreateEmptyDatabaseAsync()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(StageZeroApp.PostgresServerVariable));
        var name = $"stagezero_switch_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server.ConnectionString)
        {
            Database = "postgres", Pooling = false
        }.ConnectionString);
        await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await create.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = name };
    }

    private static object Target(NpgsqlConnectionStringBuilder db, string? password = null) => new
    {
        host = db.Host, port = db.Port, database = db.Database, username = db.Username,
        password = password ?? db.Password, sslMode = "Disable"
    };

    [Fact]
    public async Task A_fresh_install_reports_its_sqlite_file_and_offers_the_switch()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var status = await (await client.GetAsync("/api/settings/database")).ReadAsync<DatabaseStatus>();

        Assert.Equal(("sqlite", DatabaseSource.Default, true), (status.Provider, status.Source, status.CanSwitch));
        Assert.Contains("stagezero.db", status.Description);
    }

    [Fact]
    public async Task An_unreachable_server_is_reported_without_saving_anything()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var result = await (await client.PostJsonAsync("/api/settings/database/test", new
        {
            host = "127.0.0.1", port = 1, database = "stagezero", username = "stagezero", password = "x", sslMode = "Disable"
        })).ReadAsync<PostgresTestResult>();

        Assert.False(result.Ok);
        Assert.StartsWith("Couldn't connect", result.Problem);
        Assert.False(File.Exists(DatabaseChoice.FilePath(app.DataDirectory)));
    }

    [Fact]
    public async Task Missing_fields_are_refused_with_a_reason()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var response = await client.PostJsonAsync("/api/settings/database/test", new { host = "db", database = "stagezero" });

        Assert.Equal("Username is required.", await response.ProblemTitleAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Settings_need_a_signed_in_operator()
    {
        await using var app = CreateApp();
        using var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/settings/database")).StatusCode);
    }

    [Fact]
    public async Task The_first_run_calls_are_open_until_a_user_exists_then_closed()
    {
        await using var app = CreateApp();
        using var anonymous = app.CreateApiClient();

        var before = await anonymous.GetAsync("/api/setup/database");
        using var signedIn = await app.SignedInClientAsync(); // creates the admin
        var after = await anonymous.PostJsonAsync("/api/setup/database/switch", new { host = "db" });

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal("Setup has already been completed.", await after.ProblemTitleAsync(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task A_database_set_by_the_environment_is_not_switched_from_the_web_app()
    {
        await using var app = CreateApp();
        // After the app is made (it clears the variables for its SQLite run), before it starts.
        Environment.SetEnvironmentVariable(DatabaseSettings.ProviderVariable, "sqlite");
        try
        {
            using var client = await app.SignedInClientAsync();

            var status = await (await client.GetAsync("/api/settings/database")).ReadAsync<DatabaseStatus>();
            var switched = await client.PostJsonAsync("/api/settings/database/switch", new
            {
                host = "db", database = "stagezero", username = "stagezero", password = "x"
            });

            Assert.Equal((DatabaseSource.Environment, false), (status.Source, status.CanSwitch));
            Assert.Contains(DatabaseSettings.ProviderVariable, await switched.ProblemTitleAsync(HttpStatusCode.Conflict));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabaseSettings.ProviderVariable, null);
        }
    }

    [PostgresFact]
    public async Task Switching_copies_everything_saves_the_choice_encrypted_and_restarts()
    {
        var target = await CreateEmptyDatabaseAsync();
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var tested = await (await client.PostJsonAsync("/api/settings/database/test", Target(target))).ReadAsync<PostgresTestResult>();
        var switched = await client.PostJsonAsync("/api/settings/database/switch", Target(target));

        Assert.True(tested.Ok, tested.Problem);
        Assert.False(tested.HasData);
        var result = await switched.ReadAsync<DatabaseSwitchResult>();
        Assert.True(result.Restarting);
        Assert.Equal(1, _restarter.Restarts);
        Assert.DoesNotContain(target.Password!, await switched.Content.ReadAsStringAsync());

        // The admin made on SQLite is now in PostgreSQL.
        await using (var pg = DatabaseSettings.Postgres(target.ConnectionString).CreateContext())
        {
            Assert.Equal("owner@example.com", (await pg.Users.SingleAsync()).Email);
        }

        // Saved for the next start, with the password encrypted.
        var file = DatabaseChoice.FilePath(app.DataDirectory);
        Assert.DoesNotContain(target.Password!, await File.ReadAllTextAsync(file));
        var protection = app.Services.GetRequiredService<IDataProtectionProvider>();
        var next = DatabaseChoice.Resolve(app.DataDirectory, protection, Path.Combine(app.DataDirectory, "stagezero.db"));
        Assert.Equal((DatabaseProvider.Postgres, DatabaseSource.Saved), (next.Settings.Provider, next.Source));
        Assert.Equal(target.Database, new NpgsqlConnectionStringBuilder(next.Settings.ConnectionString).Database);

        await DropAsync(target);
    }

    [PostgresFact]
    public async Task A_database_that_already_has_stagezero_data_is_never_switched_into()
    {
        var target = await CreateEmptyDatabaseAsync();
        await using (var pg = DatabaseSettings.Postgres(target.ConnectionString).CreateContext())
        {
            await pg.Database.MigrateAsync();
            pg.AppSettings.Add(new StageZero.Models.AppSettings { Key = "k", Value = "v" });
            await pg.SaveChangesAsync();
        }

        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var tested = await (await client.PostJsonAsync("/api/settings/database/test", Target(target))).ReadAsync<PostgresTestResult>();
        var switched = await client.PostJsonAsync("/api/settings/database/switch", Target(target));

        Assert.True(tested.HasData);
        Assert.Contains("already has StageZero data", await switched.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Equal(0, _restarter.Restarts);
        Assert.False(File.Exists(DatabaseChoice.FilePath(app.DataDirectory)));

        await DropAsync(target);
    }

    [PostgresFact]
    public async Task A_wrong_password_is_reported_in_postgres_own_words()
    {
        var target = await CreateEmptyDatabaseAsync();
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var result = await (await client.PostJsonAsync("/api/settings/database/test", Target(target, password: "wrong")))
            .ReadAsync<PostgresTestResult>();

        Assert.False(result.Ok);
        Assert.Contains("password authentication failed", result.Problem);

        await DropAsync(target);
    }

    private static async Task DropAsync(NpgsqlConnectionStringBuilder target)
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(target.ConnectionString)
        {
            Database = "postgres", Pooling = false
        }.ConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{target.Database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
