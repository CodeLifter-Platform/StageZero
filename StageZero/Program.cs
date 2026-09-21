using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using StageZero.Application.Layout;
using StageZero.Data;
using StageZero.DataAdapters.AccessServiceTokens;
using StageZero.DataAdapters.DnsProviders;
using StageZero.DataAdapters.DnsRecords;
using StageZero.DataAdapters.IpChecks;
using StageZero.DataAdapters.Settings;
using StageZero.DataAdapters.TunnelConfigs;
using StageZero.DataAdapters.TunnelRoutes;
using StageZero.Models;
using StageZero.Services;
using StageZero.Services.Access;
using StageZero.Services.Cli;
using StageZero.Services.CodeLifter;
using StageZero.Services.Dns;
using StageZero.Services.Auth;
using StageZero.Services.Health;
using StageZero.Services.IpMonitoring;
using StageZero.Services.Tunnel;
using Microsoft.AspNetCore.HttpOverrides;
using Lifted.BlazorAuth.Basic.Services;
using Lifted.BlazorAuth.Basic.DataAdapters;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using dotenv.net;

// The container's HEALTHCHECK: first, and before anything slow.
if (args is [HealthCheckCommand.Name])
{
    Environment.ExitCode = await HealthCheckCommand.RunAsync(
        Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value));
    return;
}

// ═══════════════════════════════════════════════════════════════
// LOAD ENVIRONMENT VARIABLES FROM .env FILE
// ═══════════════════════════════════════════════════════════════

// Load .env file if it exists (for local development)
// Search in current directory and up to 5 parent directories.
// STAGEZERO_DOTENV=false skips it — the test suite sets that, so a developer's real
// settings in the repo-root .env never reach a test host.
var currentDir = Directory.GetCurrentDirectory();
var envFilePath = ".env";
var loadDotEnv = !string.Equals(
    Environment.GetEnvironmentVariable("STAGEZERO_DOTENV"), "false", StringComparison.OrdinalIgnoreCase);

// Try to find .env file in current directory or parent directories
for (int i = 0; loadDotEnv && i <= 5; i++)
{
    var testPath = Path.Combine(currentDir, envFilePath);
    if (File.Exists(testPath))
    {
        DotEnv.Load(new DotEnvOptions(
            envFilePaths: new[] { testPath },
            ignoreExceptions: false
        ));
        break;
    }
    envFilePath = Path.Combine("..", envFilePath);
}

// ═══════════════════════════════════════════════════════════════
// SERILOG CONFIGURATION
// ═══════════════════════════════════════════════════════════════

// Get platform-specific logs directory
var logsDirectory = DataPathService.GetLogsDirectory();
var logFilePath = Path.Combine(logsDirectory, "log-.txt");

// Debug in Development, Information otherwise; STAGEZERO_LOG_LEVEL overrides (LoggingSetup).
var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? "Production";
var minimumLevel = LoggingSetup.MinimumLevel(
    environmentName, Environment.GetEnvironmentVariable(LoggingSetup.LevelVariable));

Log.Logger = LoggingSetup.Configure(new LoggerConfiguration(), minimumLevel, logFilePath).CreateLogger();

try
{
    Log.Information("Starting StageZero application (log level {Level})", minimumLevel);
    Log.Information(DataPathService.GetPlatformInfo());

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // ═══════════════════════════════════════════════════════════════
    // SERVICES CONFIGURATION
    // ═══════════════════════════════════════════════════════════════

    // MudBlazor
    builder.Services.AddMudServices();

    // Razor Components
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // Entity Framework with DbContextFactory (required for Blazor Server)
    // Use platform-specific database path, or fall back to connection string from config
    var databasePath = DataPathService.GetDatabasePath();
    var connectionString = $"Data Source={databasePath}";

    Log.Information("Database path: {DatabasePath}", databasePath);

    builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
        options.UseSqlite(connectionString));

    // Register BasicAuthDbContext factory for the auth library (wrapper around ApplicationDbContext factory)
    builder.Services.AddScoped<IDbContextFactory<Lifted.BlazorAuth.Basic.Data.BasicAuthDbContext>>(sp =>
    {
        var appFactory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        return new BasicAuthDbContextFactoryWrapper(appFactory);
    });

    // HttpClient for external API calls
    builder.Services.AddHttpClient();
    builder.Services.AddHttpClient(PublicIpResolver.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));

    // ═══════════════════════════════════════════════════════════════
    // DATA PROTECTION
    // ═══════════════════════════════════════════════════════════════
    // One key ring, on the data volume rather than the user profile (ephemeral in a
    // container). Without it every restart would sign everyone out and make the stored
    // Cloudflare tokens and TOTP secrets undecryptable. See DataProtectionKeys.
    var keyRing = DataProtectionKeys.Prepare(DataPathService.GetAppDataDirectory());
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(keyRing)
        .SetApplicationName("StageZero");

    Log.Information("Data protection keys: {KeysPath}", keyRing.FullName);

    // ═══════════════════════════════════════════════════════════════
    // FORWARDED HEADERS (Cloudflare Tunnel)
    // ═══════════════════════════════════════════════════════════════
    // cloudflared forwards plain HTTP to this app while Cloudflare terminates TLS
    // at the edge. Without these the app sees http:// and generates insecure links.
    // The connector's source address is not fixed, so the known-proxy allowlists
    // are cleared.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // ═══════════════════════════════════════════════════════════════
    // DATA ADAPTERS REGISTRATION
    // ═══════════════════════════════════════════════════════════════
    builder.Services.AddScoped<IUserReader, UserReader>();
    builder.Services.AddScoped<IUserWriter, UserWriter>();
    builder.Services.AddScoped<IIpCheckReader, IpCheckReader>();
    builder.Services.AddScoped<IIpCheckWriter, IpCheckWriter>();
    builder.Services.AddScoped<ISettingsReader, SettingsReader>();
    builder.Services.AddScoped<ISettingsWriter, SettingsWriter>();
    builder.Services.AddScoped<IDnsProviderReader, DnsProviderReader>();
    builder.Services.AddScoped<IDnsProviderWriter, DnsProviderWriter>();
    builder.Services.AddScoped<IDnsRecordReader, DnsRecordReader>();
    builder.Services.AddScoped<IDnsRecordWriter, DnsRecordWriter>();
    builder.Services.AddScoped<ITunnelRouteReader, TunnelRouteReader>();
    builder.Services.AddScoped<ITunnelRouteWriter, TunnelRouteWriter>();
    builder.Services.AddScoped<ITunnelConfigReader, TunnelConfigReader>();
    builder.Services.AddScoped<ITunnelConfigWriter, TunnelConfigWriter>();
    builder.Services.AddScoped<IAccessServiceTokenReader, AccessServiceTokenReader>();
    builder.Services.AddScoped<IAccessServiceTokenWriter, AccessServiceTokenWriter>();

    // ═══════════════════════════════════════════════════════════════
    // SERVICES REGISTRATION
    // ═══════════════════════════════════════════════════════════════
    builder.Services.AddScoped<IAuthService, AuthService>();

    // StageZero does not send email. The auth library's one-time codes (password reset,
    // re-verifying a changed address) go to the server log as an unmissable banner:
    // whoever can read the log controls the server, and that is who may reset the admin.
    builder.Services.AddScoped<Lifted.BlazorAuth.Basic.Services.IEmailService, ServerLogCodeService>();

    // The optional newsletter / StageZero-updates boxes on the first-run setup form.
    // They post to codelifter.net; a blank CodeLifter__SubscriptionsUrl hides them.
    builder.Services.AddSingleton(CodeLifterSubscriptionsOptions.FromConfiguration(builder.Configuration));
    builder.Services.AddHttpClient(CodeLifterSubscriptions.HttpClientName);
    builder.Services.AddScoped<ISignupOptIn, CodeLifterSubscriptions>();
    builder.Services.AddSingleton<IIpChangeNotifier, IpChangeNotifier>();
    builder.Services.TryAddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IIpMonitorStatus, IpMonitorStatus>();

    // /healthz: Unhealthy without the database; Degraded (still 200) when the public IP
    // hasn't been confirmed lately. Status only — no detail for an anonymous caller.
    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database")
        .AddCheck<IpMonitorHealthCheck>("ip-monitor");
    builder.Services.AddSingleton<IPublicIpResolver, PublicIpResolver>();
    builder.Services.AddScoped<IIpMonitorService, IpMonitorService>();
    builder.Services.AddScoped<ICloudflareService, CloudflareService>();
    builder.Services.AddScoped<IDnsVerificationService, DnsVerificationService>();

    // ═══════════════════════════════════════════════════════════════
    // CLOUDFLARE TUNNEL SERVICES
    // ═══════════════════════════════════════════════════════════════
    builder.Services.AddScoped<ICloudflareTokenProtector, CloudflareTokenProtector>();
    builder.Services.AddScoped<ICloudflareTunnelService, CloudflareTunnelService>();
    builder.Services.AddScoped<ITunnelSyncService, TunnelSyncService>();

    // ═══════════════════════════════════════════════════════════════
    // CLOUDFLARE ACCESS SERVICES
    // ═══════════════════════════════════════════════════════════════
    builder.Services.AddScoped<ICloudflareAccessService, CloudflareAccessService>();
    builder.Services.AddScoped<IAccessProvisioningService, AccessProvisioningService>();

    // Swap this registration to route a freshly minted service token secret into a vault.
    // The default only records that a token was minted; it never writes the secret anywhere.
    builder.Services.AddScoped<IAccessSecretSink, LoggingAccessSecretSink>();

    // ═══════════════════════════════════════════════════════════════
    // BACKGROUND SERVICES REGISTRATION
    // ═══════════════════════════════════════════════════════════════
    builder.Services.AddHostedService<IpMonitorBackgroundService>();

    // ═══════════════════════════════════════════════════════════════
    // VIEWMODELS REGISTRATION
    // ═══════════════════════════════════════════════════════════════
    builder.Services.AddScoped<IAppVM, AppVM>();
    builder.Services.AddScoped<StageZero.Application.Areas.Home.IHomeViewModel, StageZero.Application.Areas.Home.HomeViewModel>();
    builder.Services.AddScoped<StageZero.Application.Areas.IpMonitor.IIpMonitorViewModel, StageZero.Application.Areas.IpMonitor.IpMonitorViewModel>();
    builder.Services.AddScoped<StageZero.Application.Areas.DnsConfig.IDnsConfigViewModel, StageZero.Application.Areas.DnsConfig.DnsConfigViewModel>();
    builder.Services.AddScoped<StageZero.Application.Areas.TunnelManagement.ITunnelManagementViewModel, StageZero.Application.Areas.TunnelManagement.TunnelManagementViewModel>();
    builder.Services.AddScoped<StageZero.Application.Areas.TunnelManagement.ITunnelSettingsViewModel, StageZero.Application.Areas.TunnelManagement.TunnelSettingsViewModel>();

    // ═══════════════════════════════════════════════════════════════
    // BUILD APPLICATION
    // ═══════════════════════════════════════════════════════════════
    var app = builder.Build();

    // Bring the database to the latest migration. A database from before migrations is
    // adopted once, with the original kept beside it as a .bak (DatabaseInitializer).
    await DatabaseInitializer.InitializeAsync(databasePath, app.Logger);

    // Tokens saved before they were encrypted are encrypted now (idempotent).
    await CloudflareTokenStore.ProtectLegacyTokensAsync(app.Services, app.Logger);

    using (var scope = app.Services.CreateScope())
    {
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Users.AnyAsync())
        {
            Log.Information("No users found. Please visit /setup to create your admin account");
        }
    }

    // Said once at startup so an operator reading `docker logs` knows where a reset code
    // will land before they ever need one.
    Log.Information(
        "Password reset: StageZero does not send email. A reset code requested on /forgot-password "
        + "is written to this log as a banner headed \"{Heading}\"; grep for it, then enter it on /reset-password",
        ServerLogCodeService.PasswordResetHeading);

    // Must run before anything that inspects the scheme or client IP, so the app
    // sees the original https:// request rather than the connector's plain HTTP hop.
    app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment())
    {
        // Re-executes /Error (Application/Areas/Errors) in a fresh scope.
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    // Only redirect in Development. In production Cloudflare terminates TLS and the
    // container listens on HTTP only, so redirecting here would loop against a
    // listener that does not exist.
    if (app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    app.UseStaticFiles();
    app.UseAntiforgery();

    app.MapHealthChecks(HealthCheckCommand.Path);

    // The auth library's pages (/login, /setup, /forgot-password, /reset-password) live in
    // another assembly. Routes.razor already lists it for in-app navigation; without this
    // the server does not know those routes, and typing the URL or reloading returns 404.
    app.MapRazorComponents<StageZero.Application.App>()
        .AddInteractiveServerRenderMode()
        .AddAdditionalAssemblies(typeof(Lifted.BlazorAuth.Basic.Components.Login).Assembly);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Public so the integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;

// ═══════════════════════════════════════════════════════════════
// BASIC AUTH DB CONTEXT FACTORY WRAPPER
// Wraps ApplicationDbContext factory to provide BasicAuthDbContext factory
// ═══════════════════════════════════════════════════════════════
public class BasicAuthDbContextFactoryWrapper : IDbContextFactory<Lifted.BlazorAuth.Basic.Data.BasicAuthDbContext>
{
    private readonly IDbContextFactory<StageZero.Data.ApplicationDbContext> _appFactory;

    public BasicAuthDbContextFactoryWrapper(IDbContextFactory<StageZero.Data.ApplicationDbContext> appFactory)
    {
        _appFactory = appFactory;
    }

    public Lifted.BlazorAuth.Basic.Data.BasicAuthDbContext CreateDbContext()
    {
        return _appFactory.CreateDbContext();
    }

    public async Task<Lifted.BlazorAuth.Basic.Data.BasicAuthDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return await _appFactory.CreateDbContextAsync(cancellationToken);
    }
}

