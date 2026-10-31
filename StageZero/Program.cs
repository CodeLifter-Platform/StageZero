using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using StageZero.Api;
using StageZero.Data;
using StageZero.DataAdapters.AccessServiceTokens;
using StageZero.DataAdapters.CloudflareCredentials;
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
using StageZero.Services.Cloudflare;
using StageZero.Services.Database;
using StageZero.Services.Database.Docker;
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

// Account recovery from the server: `StageZero reset-password <email>`. After .env, so a
// STAGEZERO_HOME or STAGEZERO_DB_* set there finds the same database the app uses.
if (args is [ResetPasswordCommand.Name, ..])
{
    Environment.ExitCode = await ResetPasswordCommand.RunAsync(DatabaseChoice.Resolve().Settings, args, Console.Out);
    return;
}

// Moving an install from its SQLite file to PostgreSQL: `StageZero copy-to-postgres`, with the
// target in STAGEZERO_DB_CONNECTION.
if (args is [CopyToPostgresCommand.Name])
{
    Environment.ExitCode = await CopyToPostgresCommand.RunAsync(
        DataPathService.GetDatabasePath(),
        Environment.GetEnvironmentVariable(DatabaseSettings.ConnectionVariable),
        Console.Out);
    return;
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

    // The JSON API the React app (web/, built into wwwroot) talks to, and its cookie session.
    builder.Services.AddStageZeroApi();
    builder.Services.AddStageZeroSession();

    // Entity Framework with DbContextFactory: SQLite in the data directory by default,
    // PostgreSQL when chosen in the web app (database.json) or STAGEZERO_DB_PROVIDER says so,
    // the variables winning (DatabaseChoice).
    var selection = DatabaseChoice.Resolve();
    var database = selection.Settings;
    Log.Information("Database: {Database} ({Source})", database.Describe(), selection.Source switch
    {
        DatabaseSource.Environment => "from the environment",
        DatabaseSource.Saved => "chosen in the web app",
        _ => "the default"
    });
    builder.Services.AddSingleton(selection);
    database.AddDbContextFactory(builder.Services);

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
    builder.Services.AddScoped<ICloudflareCredentialReader, CloudflareCredentialReader>();
    builder.Services.AddScoped<ICloudflareCredentialWriter, CloudflareCredentialWriter>();

    // ═══════════════════════════════════════════════════════════════
    // SERVICES REGISTRATION
    // ═══════════════════════════════════════════════════════════════
    builder.Services.AddScoped<IAuthService, AuthService>();

    // Brute-force limits (AuthThrottle): per-address. Each API call is its own request, so
    // ClientAddress reads the request's address; behind a proxy, UseForwardedHeaders makes
    // it the real one.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<IAuthThrottle, AuthThrottle>();
    builder.Services.AddScoped<ClientAddress>();

    // StageZero does not send email. The auth library's one-time codes (password reset,
    // re-verifying a changed address) go to the server log as an unmissable banner:
    // whoever can read the log controls the server, and that is who may reset the admin.
    builder.Services.AddScoped<Lifted.BlazorAuth.Basic.Services.IEmailService, ServerLogCodeService>();

    // The optional newsletter / StageZero-updates boxes on the first-run setup form.
    // They post to codelifter.net; a blank CodeLifter__SubscriptionsUrl hides them.
    builder.Services.AddSingleton(CodeLifterSubscriptionsOptions.FromConfiguration(builder.Configuration));
    builder.Services.AddHttpClient(CodeLifterSubscriptions.HttpClientName);
    builder.Services.AddScoped<ISignupOptIn, CodeLifterSubscriptions>();
    builder.Services.AddScoped<ICodeLifterRegistration, CodeLifterRegistration>();
    builder.Services.AddScoped<ICodeLifterManageLink, CodeLifterManageLink>();
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
    builder.Services.AddScoped<ICloudflareAccountService, CloudflareAccountService>();
    builder.Services.AddScoped<ICloudflareCredentialService, CloudflareCredentialService>();
    builder.Services.AddScoped<IDatabaseSwitchService, DatabaseSwitchService>();
    builder.Services.AddSingleton<IAppRestarter, HostAppRestarter>();

    // Settings → Database, "Use PostgreSQL": find servers on the machine, or create StageZero's
    // own container. The Docker socket is there only when the operator mounted it
    // (docker-compose.provision.yml), and the page says what that grants.
    builder.Services.AddSingleton<IDockerApi>(new DockerApi(DockerEndpoint.Discover()));
    builder.Services.AddSingleton(AppContainer.Detect());
    builder.Services.AddSingleton<IPostgresProber, NpgsqlProber>();
    builder.Services.AddSingleton<IPostgresAdmin, NpgsqlAdmin>();
    builder.Services.AddScoped<PostgresDiscovery>();
    builder.Services.AddScoped<PostgresProvisioner>();
    builder.Services.AddScoped<ICloudflareTunnelService, CloudflareTunnelService>();
    builder.Services.AddScoped<ITunnelSyncService, TunnelSyncService>();
    builder.Services.AddScoped<ITunnelSetupService, TunnelSetupService>();
    builder.Services.AddScoped<ITunnelRouteService, TunnelRouteService>();
    builder.Services.AddScoped<IDnsConfigService, DnsConfigService>();

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
    // BUILD APPLICATION
    // ═══════════════════════════════════════════════════════════════
    var app = builder.Build();

    // Bring the database to the latest migration. A database from before migrations is
    // adopted once, with the original kept beside it as a .bak (DatabaseInitializer).
    await DatabaseInitializer.InitializeAsync(database, app.Logger);

    // Tokens saved before they were encrypted are encrypted now (idempotent).
    await CloudflareTokenStore.ProtectLegacyTokensAsync(app.Services, app.Logger);

    using (var scope = app.Services.CreateScope())
    {
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Users.AnyAsync())
        {
            Log.Information("No users found. Open the app in a browser to create your admin account");
        }
    }

    // Said once at startup so an operator reading `docker logs` knows where a reset code
    // will land before they ever need one.
    Log.Information(
        "Password reset: StageZero does not send email. A reset code requested on the Forgot Password page "
        + "is written to this log as a banner headed \"{Heading}\"; grep for it, then enter it on the Reset Password page",
        ServerLogCodeService.PasswordResetHeading);

    // Must run before anything that inspects the scheme or client IP, so the app
    // sees the original https:// request rather than the connector's plain HTTP hop.
    app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment())
    {
        // Problem details with a reference (the trace ID) and nothing from the exception.
        app.UseExceptionHandler();
        app.UseHsts();
    }

    // Only redirect in Development, and only when there is an HTTPS listener to redirect to
    // (`dotnet run`, the VS Code launch). The containers listen on plain HTTP only — release
    // because Cloudflare terminates TLS, debug because it is localhost — so there the
    // redirect would point at a listener that does not exist.
    if (app.Environment.IsDevelopment() && HttpsListener.IsConfigured(app.Configuration))
    {
        app.UseHttpsRedirection();
    }

    app.UseApiRequestGuard();
    app.UseSpaStaticFiles();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks(HealthCheckCommand.Path);

    var api = app.MapGroup(ApiPipeline.Prefix);
    api.MapAuthEndpoints();
    api.MapIpEndpoints();
    api.MapDnsEndpoints();
    api.MapTunnelEndpoints();
    api.MapSettingsEndpoints();
    api.MapDatabaseEndpoints();

    // Everything else is the React app's: deep links and reloads get index.html.
    app.MapSpaFallback();

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

