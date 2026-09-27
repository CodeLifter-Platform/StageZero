using Serilog.Events;
using StageZero.Services;

namespace StageZero.Tests;

public class LoggingSetupTests
{
    [Theory]
    [InlineData("Production", null, LogEventLevel.Information)]
    [InlineData("Staging", null, LogEventLevel.Information)]
    [InlineData(null, null, LogEventLevel.Information)]
    [InlineData("Development", null, LogEventLevel.Debug)]
    [InlineData("Production", "Debug", LogEventLevel.Debug)]
    [InlineData("Production", "warning", LogEventLevel.Warning)]
    [InlineData("Development", "Error", LogEventLevel.Error)]
    [InlineData("Production", "loud", LogEventLevel.Information)]
    [InlineData("Production", "42", LogEventLevel.Information)]
    public void Production_logs_information_unless_told_otherwise(string? environment, string? configured, LogEventLevel expected)
    {
        Assert.Equal(expected, LoggingSetup.MinimumLevel(environment, configured));
    }
}
