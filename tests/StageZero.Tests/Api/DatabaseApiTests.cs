using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StageZero.Data;
using StageZero.Services.Database;
using StageZero.Tests.Fakes;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Api;

/// <summary>
/// The Database page over HTTP: behind the sign-in, honest about where the data is and who
/// decides, and — with a PostgreSQL server to hand — the whole move: copy, stored choice,
/// restart, and the next start running on PostgreSQL with the same account.
/// </summary>
[Collection(AppCollection.Name)]
public class DatabaseApiTests
{
    [Fact]
    public async Task The_database_page_needs_a_sign_in()
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/database")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostJsonAsync("/api/database/test", new { connectionString = "x" })).StatusCode);
    }

    [Fact]
    public async Task Status_says_where_the_data_is_and_who_decided()
    {
        await using var app = new StageZeroApp();
        using var client = await app.SignedInClientAsync();

        var status = await (await client.GetAsync("/api/database")).ReadAsync<DatabaseStatus>();

        if (app.IsPostgres)
        {
            // The PostgreSQL leg sets the variables: the page can look but not change.
            Assert.Equal((DatabaseProvider.Postgres, DatabaseSettingsSource.Environment, false), (status.Provider, status.Source, status.CanChange));
        }
        else
        {
            Assert.Equal((DatabaseProvider.Sqlite, DatabaseSettingsSource.Default, true), (status.Provider, status.Source, status.CanChange));
            Assert.True(status.SqliteFileExists);
        }

        Assert.DoesNotContain("Password", status.Description);
    }

    [Fact]
    public async Task A_bad_connection_string_is_refused_with_a_message_for_the_operator()
    {
        await using var app = new StageZeroApp();
        using var client = await app.SignedInClientAsync();

        var response = await client.PostJsonAsync("/api/database/test", new { connectionString = "not a connection string" });

        Assert.Contains("isn't a PostgreSQL connection string", await response.ProblemTitleAsync(HttpStatusCode.BadRequest));
    }

    [PostgresFact]
    public async Task Switching_copies_the_data_records_the_choice_and_the_next_start_runs_on_PostgreSQL()
    {
        var server = Environment.GetEnvironmentVariable(StageZeroApp.PostgresServerVariable)!;
        var target = new NpgsqlConnectionStringBuilder(server) { Database = $"stagezero_switch_{Guid.NewGuid():N}" }.ToString();
        var restart = new FakeAppRestart();
        try
        {
            // 1. A SQLite install with an account, as the product starts out of the box.
            await using var first = new StageZeroApp(fromDataDirectory: true)
            {
                ConfigureServices = services => services.Replace(ServiceDescriptor.Singleton<IAppRestart>(restart))
            };
            using var client = await first.SignedInClientAsync();
            Assert.Equal(DatabaseProvider.Sqlite, (await (await client.GetAsync("/api/database")).ReadAsync<DatabaseStatus>()).Provider);

            // The database isn't there yet: the server is reached, and the switch will create it.
            var tested = await (await client.PostJsonAsync("/api/database/test", new { connectionString = target }))
                .ReadAsync<ConnectionTest>();
            Assert.Equal((false, true, false), (tested.DatabaseExists, tested.IsEmpty, tested.HasStageZeroSchema));
            Assert.Equal(new NpgsqlConnectionStringBuilder(target).Database, tested.Database);

            // 2. The switch: rows copied, the choice stored encrypted, a restart asked for.
            var switched = await (await client.PostJsonAsync("/api/database/switch", new { connectionString = target, copyData = true }))
                .ReadAsync<SwitchResult>();
            Assert.Contains(switched.Copied, c => c is { Table: "User", Rows: 1 });
            Assert.Single(restart.Reasons);
            var file = File.ReadAllText(DatabaseSettingsFile.PathIn(first.DataDirectory));
            Assert.DoesNotContain(new NpgsqlConnectionStringBuilder(target).Password!, file);
            Assert.DoesNotContain(target, file);

            // While the restart is pending the page is read-only.
            var again = await client.PostJsonAsync("/api/database/switch", new { connectionString = target, copyData = true });
            Assert.Equal("StageZero is already restarting.", await again.ProblemTitleAsync(HttpStatusCode.Conflict));

            // 3. The next start, from the same data directory with nothing in the environment.
            await using var second = new StageZeroApp(dataDirectory: first.DataDirectory, fromDataDirectory: true);
            using var secondClient = second.CreateApiClient();
            var login = await secondClient.PostJsonAsync("/api/auth/login", new { email = "owner@example.com", password = TestApi.Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            // Tested again, the database is there and already StageZero's.
            var retested = await (await secondClient.PostJsonAsync("/api/database/test", new { connectionString = target }))
                .ReadAsync<ConnectionTest>();
            Assert.Equal((true, false, true), (retested.DatabaseExists, retested.IsEmpty, retested.HasStageZeroSchema));

            var status = await (await secondClient.GetAsync("/api/database")).ReadAsync<DatabaseStatus>();
            Assert.Equal((DatabaseProvider.Postgres, DatabaseSettingsSource.Settings, true), (status.Provider, status.Source, status.CanChange));
            Assert.Contains(new NpgsqlConnectionStringBuilder(target).Database!, status.Description);

            await using (var db = await second.CreateDbContextAsync())
            {
                Assert.True(db.Database.IsNpgsql());
                Assert.Equal("owner@example.com", (await db.Users.SingleAsync()).Email);
            }

            // 4. Going back forgets the choice; the SQLite file is still there for it.
            var reverted = await (await secondClient.PostJsonAsync("/api/database/revert")).ReadAsync<SwitchResult>();
            Assert.Contains("SQLite", reverted.Message);
            Assert.False(File.Exists(DatabaseSettingsFile.PathIn(first.DataDirectory)));
            Assert.True(File.Exists(Path.Combine(first.DataDirectory, "stagezero.db")));
        }
        finally
        {
            await DropAsync(target);
        }
    }

    private static async Task DropAsync(string connection)
    {
        NpgsqlConnection.ClearAllPools();
        var target = new NpgsqlConnectionStringBuilder(connection);
        var maintenance = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres", Pooling = false };
        await using var admin = new NpgsqlConnection(maintenance.ConnectionString);
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{target.Database}\" WITH (FORCE);";
        await drop.ExecuteNonQueryAsync();
    }
}
