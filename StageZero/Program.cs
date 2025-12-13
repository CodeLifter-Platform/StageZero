using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using StageZero.Application.Layout;
using StageZero.Data;
using StageZero.DataAdapters.DnsProviders;
using StageZero.DataAdapters.DnsRecords;
using StageZero.DataAdapters.IpChecks;
using StageZero.DataAdapters.Settings;
using StageZero.DataAdapters.Users;
using StageZero.Models;
using StageZero.Services.Auth;
using StageZero.Services.Dns;
using StageZero.Services.Email;
using StageZero.Services.IpMonitoring;
using StageZero.ReverseProxy.Services;
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

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting StageZero application");

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
    builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

    // HttpClient for external API calls
    builder.Services.AddHttpClient();

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

    // ═══════════════════════════════════════════════════════════════
    // SERVICES REGISTRATION
    // ═══════════════════════════════════════════════════════════════
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<IIpMonitorService, IpMonitorService>();
    builder.Services.AddScoped<ICloudflareService, CloudflareService>();
    builder.Services.AddScoped<IDnsUpdateService, DnsUpdateService>();
    builder.Services.AddScoped<IDnsVerificationService, DnsVerificationService>();

    // Register DbContext as base class for reverse proxy library
    builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

    // Register Proxy Configuration Service (stub for now - will be replaced when YARP is fully integrated)
    builder.Services.AddScoped<IProxyConfigurationService, StubProxyConfigurationService>();

    // Register Proxy Host Manager
    builder.Services.AddScoped<IProxyHostManager, ProxyHostManager>();

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
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not add email verification columns (may already exist)");
        }

        // Seed default admin user if no users exist
        if (!await db.Users.AnyAsync())
        {
            Log.Information("No users found. Creating default admin user");
            var adminUser = new User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                Email = "admin@stagezero.local",
                IsActive = true,
                RequiresPasswordChange = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(adminUser);
            await db.SaveChangesAsync();
            Log.Information("Default admin user created (username: admin, password: admin)");
        }
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
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
// STUB PROXY CONFIGURATION SERVICE
// This is a temporary stub until YARP reverse proxy is fully integrated
// ═══════════════════════════════════════════════════════════════
public class StubProxyConfigurationService : IProxyConfigurationService
{
    public Task ReloadConfigurationAsync()
    {
        // Stub implementation - does nothing for now
        // Will be replaced with actual YARP configuration when reverse proxy is fully integrated
        return Task.CompletedTask;
    }
}

