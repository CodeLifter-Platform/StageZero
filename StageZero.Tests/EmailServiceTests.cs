using Microsoft.Extensions.Logging;
using StageZero.Services.Email;
using StageZero.Tests.Fakes;

namespace StageZero.Tests;

public class EmailServiceTests
{
    /// <summary>Captures log lines so a test can assert on what the operator would see.</summary>
    private sealed class CapturingLogger : ILogger<EmailService>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }

    private static (EmailService Service, CapturingLogger Log) Create(EmailOptions options)
    {
        var log = new CapturingLogger();
        return (new EmailService(options, log), log);
    }

    private static EmailOptions RelayOptions(FakeSmtpServer server, string? username = null) => new()
    {
        SmtpHost = "127.0.0.1",
        SmtpPort = server.Port,
        FromEmail = "stagezero@example.test",
        FromName = "StageZero",
        SmtpUsername = username,
        SmtpPassword = username is null ? null : "secret",
        UseStartTls = false,
    };

    // ── Not configured ────────────────────────────────────────────

    [Fact]
    public async Task Unconfigured_service_reports_itself_and_writes_the_code_to_the_log()
    {
        var (service, log) = Create(new EmailOptions());

        Assert.False(await service.IsConfiguredAsync());

        await service.SendVerificationCodeAsync("admin@example.test", "123456");
        await service.SendPasswordResetCodeAsync("admin@example.test", "654321");

        var warnings = log.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, m => m.Contains("verification code: 123456", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, m => m.Contains("password reset code: 654321", StringComparison.OrdinalIgnoreCase));
        Assert.All(warnings, m => Assert.Contains("Email__SmtpHost", m));
        Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Error);
    }

    // ── Plaintext relay, no authentication ────────────────────────

    [Fact]
    public async Task Verification_code_is_delivered_through_a_plaintext_relay()
    {
        await using var server = new FakeSmtpServer();
        var (service, log) = Create(RelayOptions(server));

        Assert.True(await service.IsConfiguredAsync());
        await service.SendVerificationCodeAsync("admin@example.test", "424242");

        var message = Assert.Single(server.Messages);
        Assert.Equal("stagezero@example.test", message.From);
        Assert.Equal(new[] { "admin@example.test" }, message.To);
        Assert.Equal("StageZero - Email Verification Code", message.Header("Subject"));
        Assert.Contains("424242", message.DecodedBody);
        Assert.Contains("Email Verification", message.DecodedBody);

        // The relay advertised neither STARTTLS nor AUTH, and the client asked for neither.
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("STARTTLS", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("sent to admin@example.test"));
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("424242") && e.Level != LogLevel.Information);
    }

    [Fact]
    public async Task Password_reset_code_is_delivered_with_its_own_subject()
    {
        await using var server = new FakeSmtpServer();
        var (service, _) = Create(RelayOptions(server));

        await service.SendPasswordResetCodeAsync("someone@example.test", "777777");

        var message = Assert.Single(server.Messages);
        Assert.Equal("StageZero - Password Reset Code", message.Header("Subject"));
        Assert.Contains("777777", message.DecodedBody);
        Assert.Contains("Password Reset Request", message.DecodedBody);
    }

    // ── Failures name the server and its reason ───────────────────

    [Fact]
    public async Task A_rejected_recipient_surfaces_the_server_reason_and_the_relay_address()
    {
        await using var server = new FakeSmtpServer(rejectRecipients: true);
        var (service, log) = Create(RelayOptions(server));

        var ex = await Assert.ThrowsAsync<EmailServiceException>(
            () => service.SendVerificationCodeAsync("nobody@example.test", "111111"));

        Assert.Contains($"127.0.0.1:{server.Port}", ex.Message);
        Assert.Contains("verification", ex.Message);
        Assert.NotNull(ex.InnerException);
        Assert.Empty(server.Messages);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Exception is not null);
    }

    [Fact]
    public async Task StartTls_against_a_relay_that_cannot_do_it_fails_with_the_reason()
    {
        await using var server = new FakeSmtpServer();
        var options = new EmailOptions
        {
            SmtpHost = "127.0.0.1",
            SmtpPort = server.Port,
            FromEmail = "stagezero@example.test",
            UseStartTls = true,
        };
        var (service, _) = Create(options);

        var ex = await Assert.ThrowsAsync<EmailServiceException>(
            () => service.SendVerificationCodeAsync("admin@example.test", "222222"));

        // The old behaviour hid this behind "Could not send verification email".
        Assert.Contains("secure connection", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(server.Messages);
    }

    [Fact]
    public async Task A_refused_connection_names_the_relay_address()
    {
        int freePort;
        await using (var probe = new FakeSmtpServer())
        {
            freePort = probe.Port;
        }

        var (service, _) = Create(new EmailOptions
        {
            SmtpHost = "127.0.0.1",
            SmtpPort = freePort,
            FromEmail = "stagezero@example.test",
            UseStartTls = false,
        });

        var ex = await Assert.ThrowsAsync<EmailServiceException>(
            () => service.SendVerificationCodeAsync("admin@example.test", "333333"));

        Assert.Contains($"127.0.0.1:{freePort}", ex.Message);
    }
}
