using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StageZero.Data;
using StageZero.Services;

namespace StageZero.Tests.Infrastructure;

/// <summary>
/// The real app, hosted in memory against a throwaway data directory. One instance per test:
/// the data directory travels through a process-wide environment variable, so every test
/// that hosts the app belongs to the <see cref="AppCollection"/>, which runs sequentially.
/// <para>
/// With <c>STAGEZERO_TEST_POSTGRES</c> set to a server's connection string (CI's PostgreSQL
/// leg), each instance gets its own throwaway PostgreSQL database instead of the SQLite file,
/// so the same tests prove the app on both providers.
/// </para>
/// </summary>
public sealed class StageZeroApp : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } =
        Directory.CreateTempSubdirectory("stagezero-tests-").FullName;

    /// <summary>What every outbound HTTP request gets instead of the internet.</summary>
    public FakeHttp Http { get; } = new();

    public const string PostgresServerVariable = "STAGEZERO_TEST_POSTGRES";

    /// <summary>The database this instance runs on.</summary>
    public DatabaseSettings Database { get; }

    public bool IsPostgres => Database.Provider == DatabaseProvider.Postgres;

    private readonly string _environment;

    public StageZeroApp(string environment = "Development")
    {
        _environment = environment;
        Environment.SetEnvironmentVariable(DataPathService.HomeVariable, DataDirectory);

        var server = Environment.GetEnvironmentVariable(PostgresServerVariable);
        if (string.IsNullOrWhiteSpace(server))
        {
            Database = DatabaseSettings.Sqlite(Path.Combine(DataDirectory, "stagezero.db"));
            Environment.SetEnvironmentVariable(DatabaseSettings.ProviderVariable, null);
            Environment.SetEnvironmentVariable(DatabaseSettings.ConnectionVariable, null);
        }
        else
        {
            var connection = new NpgsqlConnectionStringBuilder(server)
            {
                Database = $"stagezero_test_{Guid.NewGuid():N}"
            }.ToString();
            Database = DatabaseSettings.Postgres(connection);
            Environment.SetEnvironmentVariable(DatabaseSettings.ProviderVariable, "postgres");
            Environment.SetEnvironmentVariable(DatabaseSettings.ConnectionVariable, connection);
        }
    }

    /// <summary>Replaces registrations after the app's own (fakes for Cloudflare, a failing store).</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    /// <summary>Serves the web app from here instead of the project's wwwroot.</summary>
    public string? WebRoot { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        if (WebRoot is not null)
        {
            builder.UseWebRoot(WebRoot);
        }

        builder.ConfigureTestServices(services =>
        {
            // The background services reach the internet (ipify, Cloudflare). Tests drive
            // those services directly instead.
            var background = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                         && d.ImplementationType?.Assembly == typeof(Program).Assembly)
                .ToList();
            foreach (var descriptor in background)
            {
                services.Remove(descriptor);
            }

            // No real network: every HttpClient the app makes talks to FakeHttp.
            services.ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => Http.CreateHandler()));

            ConfigureServices?.Invoke(services);
        });
    }

    /// <summary>A client that reports redirects instead of following them.</summary>
    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task<ApplicationDbContext> CreateDbContextAsync()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (IsPostgres)
        {
            await DropPostgresDatabaseAsync();
        }

        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A log file still being flushed; the OS temp cleaner will have it.
        }
    }

    private async Task DropPostgresDatabaseAsync()
    {
        NpgsqlConnection.ClearAllPools();
        var target = new NpgsqlConnectionStringBuilder(Database.ConnectionString);
        var maintenance = new NpgsqlConnectionStringBuilder(target.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{target.Database}\" WITH (FORCE);";
        await drop.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class AppCollection
{
    public const string Name = "App";
}

internal static class TestEnvironment
{
    /// <summary>
    /// Runs before any test. The repo-root .env holds a developer's real SMTP credentials,
    /// and the app walks up from the working directory looking for one.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("STAGEZERO_DOTENV", "false");
    }
}
