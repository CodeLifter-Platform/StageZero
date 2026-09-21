using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StageZero.Data;
using StageZero.Services;

namespace StageZero.Tests.Infrastructure;

/// <summary>
/// The real app, hosted in memory against a throwaway data directory. One instance per test:
/// the data directory travels through a process-wide environment variable, so every test
/// that hosts the app belongs to the <see cref="AppCollection"/>, which runs sequentially.
/// </summary>
public sealed class StageZeroApp : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } =
        Directory.CreateTempSubdirectory("stagezero-tests-").FullName;

    /// <summary>What every outbound HTTP request gets instead of the internet.</summary>
    public FakeHttp Http { get; } = new();

    public StageZeroApp()
    {
        Environment.SetEnvironmentVariable(DataPathService.HomeVariable, DataDirectory);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
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
        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A log file still being flushed; the OS temp cleaner will have it.
        }
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
