using Serilog;
using Serilog.Events;

namespace StageZero.Services;

/// <summary>
/// How much StageZero logs. Debug in Development, Information everywhere else, and
/// <c>STAGEZERO_LOG_LEVEL</c> (Verbose, Debug, Information, Warning, Error, Fatal) overrides
/// both. Framework chatter stays at Warning whatever the level: ASP.NET Core's request lines
/// would carry sign-in tickets in their URLs, and HttpClient's would add four lines to every
/// IP check.
/// </summary>
public static class LoggingSetup
{
    public const string LevelVariable = "STAGEZERO_LOG_LEVEL";

    public static LogEventLevel MinimumLevel(string? environment, string? configured) =>
        Enum.TryParse<LogEventLevel>(configured, ignoreCase: true, out var level) && Enum.IsDefined(level)
            ? level
            : string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
                ? LogEventLevel.Debug
                : LogEventLevel.Information;

    public static LoggerConfiguration Configure(LoggerConfiguration logger, LogEventLevel minimum, string logFilePath) =>
        logger
            .MinimumLevel.Is(minimum)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("MudBlazor", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 31);
}
