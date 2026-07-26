using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using StageZero.Application.Layout;
using StageZero.Data;
using StageZero.DataAdapters.DnsProviders;
using StageZero.DataAdapters.DnsRecords;
using StageZero.DataAdapters.IpChecks;
using StageZero.DataAdapters.Settings;
using StageZero.DataAdapters.TunnelConfigs;
using StageZero.DataAdapters.TunnelRoutes;
using StageZero.Models;
using StageZero.Services;
using StageZero.Services.Dns;
using StageZero.Services.Email;
using StageZero.Services.IpMonitoring;
using StageZero.Services.Tunnel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Lifted.BlazorAuth.Basic.Services;
using Lifted.BlazorAuth.Basic.DataAdapters;
using Serilog;
using dotenv.net;

// ═══════════════════════════════════════════════════════════════
// LOAD ENVIRONMENT VARIABLES FROM .env FILE
// ═══════════════════════════════════════════════════════════════

// Load .env file if it exists (for local development)
// Search in current directory and up to 5 parent directories
var currentDir = Directory.GetCurrentDirectory();
var envFilePath = ".env";

// Try to find .env file in current directory or parent directories
for (int i = 0; i <= 5; i++)
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

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting StageZero application");
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

    // ═══════════════════════════════════════════════════════════════
    // DATA PROTECTION
    // ═══════════════════════════════════════════════════════════════
    // Keys must live on the mounted data volume, not the container filesystem.
    // Otherwise the encrypted Cloudflare API token in TunnelConfig becomes
    // undecryptable after the next down/up cycle.
    var dataProtectionKeysPath = Path.Combine(DataPathService.GetAppDataDirectory(), "dp-keys");
    Directory.CreateDirectory(dataProtectionKeysPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
        .SetApplicationName("StageZero");

    Log.Information("Data protection keys: {KeysPath}", dataProtectionKeysPath);

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

    // ═══════════════════════════════════════════════════════════════
    // SERVICES REGISTRATION
    // ═══════════════════════════════════════════════════════════════
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<StageZero.Services.Email.IEmailService, StageZero.Services.Email.EmailService>();
    builder.Services.AddScoped<Lifted.BlazorAuth.Basic.Services.IEmailService, StageZero.Services.Email.EmailService>();
    builder.Services.AddScoped<IIpMonitorService, IpMonitorService>();
    builder.Services.AddScoped<ICloudflareService, CloudflareService>();
    builder.Services.AddScoped<IDnsUpdateService, DnsUpdateService>();
    builder.Services.AddScoped<IDnsVerificationService, DnsVerificationService>();

    // ═══════════════════════════════════════════════════════════════
    // CLOUDFLARE TUNNEL SERVICES
    // ═══════════════════════════════════════════════════════════════
    builder.Services.AddScoped<ITunnelTokenProtector, TunnelTokenProtector>();
    builder.Services.AddScoped<ICloudflareTunnelService, CloudflareTunnelService>();
    builder.Services.AddScoped<ITunnelSyncService, TunnelSyncService>();

    // ═══════════════════════════════════════════════════════════════
    // BACKGROUND SERVICES REGISTRATION
    // ═══════════════════════════════════════════════════════════════
    builder.Services.AddHostedService<IpMonitorBackgroundService>();
    builder.Services.AddHostedService<IpChangeHandlerService>();

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

    // Ensure database is created and seed default user
    using (var scope = app.Services.CreateScope())
    {
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();

        // Add RequiresPasswordChange column if it doesn't exist (for existing databases)
        try
        {
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*)
                FROM pragma_table_info('Users')
                WHERE name='RequiresPasswordChange'";
            var columnExists = (long)(await command.ExecuteScalarAsync() ?? 0L) > 0;

            if (!columnExists)
            {
                Log.Information("Adding RequiresPasswordChange column to Users table");
                command.CommandText = "ALTER TABLE Users ADD COLUMN RequiresPasswordChange INTEGER NOT NULL DEFAULT 0";
                await command.ExecuteNonQueryAsync();
                Log.Information("RequiresPasswordChange column added successfully");

                // Update existing admin user with default password to require password change
                command.CommandText = @"
                    UPDATE Users
                    SET RequiresPasswordChange = 1
                    WHERE Username = 'admin'";
                var rowsAffected = await command.ExecuteNonQueryAsync();
                if (rowsAffected > 0)
                {
                    Log.Information("Updated existing admin user to require password change");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not add RequiresPasswordChange column (may already exist)");
        }

        // Add email verification columns if they don't exist (for existing databases)
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            using var command = connection.CreateCommand();

            // Check and add EmailVerified column
            command.CommandText = @"
                SELECT COUNT(*)
                FROM pragma_table_info('Users')
                WHERE name='EmailVerified'";
            var emailVerifiedExists = (long)(await command.ExecuteScalarAsync() ?? 0L) > 0;

            if (!emailVerifiedExists)
            {
                Log.Information("Adding EmailVerified column to Users table");
                command.CommandText = "ALTER TABLE Users ADD COLUMN EmailVerified INTEGER NOT NULL DEFAULT 0";
                await command.ExecuteNonQueryAsync();
                Log.Information("EmailVerified column added successfully");
            }

            // Check and add EmailVerificationCode column
            command.CommandText = @"
                SELECT COUNT(*)
                FROM pragma_table_info('Users')
                WHERE name='EmailVerificationCode'";
            var codeExists = (long)(await command.ExecuteScalarAsync() ?? 0L) > 0;

            if (!codeExists)
            {
                Log.Information("Adding EmailVerificationCode column to Users table");
                command.CommandText = "ALTER TABLE Users ADD COLUMN EmailVerificationCode TEXT";
                await command.ExecuteNonQueryAsync();
                Log.Information("EmailVerificationCode column added successfully");
            }

            // Check and add EmailVerificationCodeExpiry column
            command.CommandText = @"
                SELECT COUNT(*)
                FROM pragma_table_info('Users')
                WHERE name='EmailVerificationCodeExpiry'";
            var expiryExists = (long)(await command.ExecuteScalarAsync() ?? 0L) > 0;

            if (!expiryExists)
            {
                Log.Information("Adding EmailVerificationCodeExpiry column to Users table");
                command.CommandText = "ALTER TABLE Users ADD COLUMN EmailVerificationCodeExpiry TEXT";
                await command.ExecuteNonQueryAsync();
                Log.Information("EmailVerificationCodeExpiry column added successfully");
            }

            // Check and add PasswordResetCode column
            command.CommandText = @"
                SELECT COUNT(*)
                FROM pragma_table_info('Users')
                WHERE name='PasswordResetCode'";
            var passwordResetCodeExists = (long)(await command.ExecuteScalarAsync() ?? 0L) > 0;

            if (!passwordResetCodeExists)
            {
                Log.Information("Adding PasswordResetCode column to Users table");
                command.CommandText = "ALTER TABLE Users ADD COLUMN PasswordResetCode TEXT";
                await command.ExecuteNonQueryAsync();
                Log.Information("PasswordResetCode column added successfully");
            }

            // Check and add PasswordResetCodeExpiry column
            command.CommandText = @"
                SELECT COUNT(*)
                FROM pragma_table_info('Users')
                WHERE name='PasswordResetCodeExpiry'";
            var passwordResetExpiryExists = (long)(await command.ExecuteScalarAsync() ?? 0L) > 0;

            if (!passwordResetExpiryExists)
            {
                Log.Information("Adding PasswordResetCodeExpiry column to Users table");
                command.CommandText = "ALTER TABLE Users ADD COLUMN PasswordResetCodeExpiry TEXT";
                await command.ExecuteNonQueryAsync();
                Log.Information("PasswordResetCodeExpiry column added successfully");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not add email verification and password reset columns (may already exist)");
        }

        // Migrate the retired reverse proxy schema to the Cloudflare Tunnel schema.
        // ProxyHosts belonged to the YARP/Let's Encrypt layer that Cloudflare Tunnel
        // replaces; the proxy never actually routed traffic, so no data is lost.
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            using var command = connection.CreateCommand();

            command.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table' AND name='ProxyHosts'";
            var proxyHostsExists = (long)(await command.ExecuteScalarAsync() ?? 0L) > 0;

            if (proxyHostsExists)
            {
                Log.Information("Dropping retired ProxyHosts table (replaced by TunnelRoutes)");
                command.CommandText = "DROP TABLE ProxyHosts";
                await command.ExecuteNonQueryAsync();
            }

            command.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table' AND name='TunnelRoutes'";
            var tunnelRoutesExists = (long)(await command.ExecuteScalarAsync() ?? 0L) > 0;

            if (!tunnelRoutesExists)
            {
                Log.Information("Creating TunnelRoutes table");
                command.CommandText = @"
                    CREATE TABLE TunnelRoutes (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DomainName TEXT NOT NULL,
                        ForwardScheme TEXT NOT NULL,
                        ForwardHost TEXT NOT NULL,
                        ForwardPort INTEGER NOT NULL,
                        IsEnabled INTEGER NOT NULL DEFAULT 1,
                        Notes TEXT,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    )";
                await command.ExecuteNonQueryAsync();

                command.CommandText = "CREATE UNIQUE INDEX IX_TunnelRoutes_DomainName ON TunnelRoutes (DomainName)";
                await command.ExecuteNonQueryAsync();

                command.CommandText = "CREATE INDEX IX_TunnelRoutes_IsEnabled ON TunnelRoutes (IsEnabled)";
                await command.ExecuteNonQueryAsync();

                Log.Information("TunnelRoutes table created successfully");
            }

            command.CommandText = @"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table' AND name='TunnelConfigs'";
            var tunnelConfigsExists = (long)(await command.ExecuteScalarAsync() ?? 0L) > 0;

            if (!tunnelConfigsExists)
            {
                Log.Information("Creating TunnelConfigs table");
                command.CommandText = @"
                    CREATE TABLE TunnelConfigs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        CloudflareAccountId TEXT NOT NULL,
                        CloudflareZoneId TEXT,
                        CloudflareZoneName TEXT,
                        ProtectedApiToken TEXT NOT NULL,
                        TunnelId TEXT,
                        TunnelName TEXT,
                        UpdatedAt TEXT NOT NULL
                    )";
                await command.ExecuteNonQueryAsync();

                Log.Information("TunnelConfigs table created successfully");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not migrate the tunnel schema");
        }

        // Seed default admin user if no users exist
        if (!await db.Users.AnyAsync())
        {
            Log.Information("No users found. Please visit /setup to create your admin account");
        }
    }

    // Must run before anything that inspects the scheme or client IP, so the app
    // sees the original https:// request rather than the connector's plain HTTP hop.
    app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
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

    app.MapRazorComponents<StageZero.Application.App>()
        .AddInteractiveServerRenderMode();

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

