using Microsoft.Extensions.Logging;
using StageZero.Services.Auth;

namespace StageZero.Tests;

public class ServerLogCodeServiceTests
{
    private sealed class CapturingLogger : ILogger<ServerLogCodeService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task Never_claims_to_send_email()
    {
        var service = new ServerLogCodeService(new CapturingLogger());

        Assert.False(await service.IsConfiguredAsync());
        // The pages quote these, so they must be the banner headings verbatim.
        Assert.Equal(ServerLogCodeService.PasswordResetHeading, service.LoggedPasswordResetCodeHeading);
        Assert.Equal(ServerLogCodeService.VerificationHeading, service.LoggedVerificationCodeHeading);
    }

    [Fact]
    public async Task Password_reset_code_is_one_unmissable_warning_naming_the_account_and_the_page()
    {
        var log = new CapturingLogger();
        var service = new ServerLogCodeService(log);

        await service.SendPasswordResetCodeAsync("admin@example.test", "482913");

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(ServerLogCodeService.PasswordResetHeading, entry.Message);
        Assert.Contains("Account:  admin@example.test", entry.Message);
        Assert.Contains("Code:     482913", entry.Message);
        Assert.Contains("/reset-password", entry.Message);
        Assert.Contains("does not send email", entry.Message);
        // A ruled box, so it stands out from the surrounding lines.
        Assert.True(entry.Message.Split('\n').Count(l => l.StartsWith("=====")) == 2);
    }

    [Fact]
    public async Task Verification_code_uses_its_own_heading()
    {
        var log = new CapturingLogger();
        var service = new ServerLogCodeService(log);

        await service.SendVerificationCodeAsync("admin@example.test", "111222");

        var entry = Assert.Single(log.Entries);
        Assert.Contains(ServerLogCodeService.VerificationHeading, entry.Message);
        Assert.DoesNotContain(ServerLogCodeService.PasswordResetHeading, entry.Message);
        Assert.Contains("Code:     111222", entry.Message);
    }

    [Fact]
    public void Banner_is_greppable_by_its_heading_and_by_the_words_password_reset_code()
    {
        var banner = ServerLogCodeService.FormatBanner(ServerLogCodeService.PasswordResetHeading, "a@b.c", "000000", "x");

        Assert.Contains("PASSWORD RESET CODE", banner);
        Assert.Contains("PASSWORD RESET CODE", banner.ToUpperInvariant());
    }
}
