using Microsoft.Extensions.Configuration;
using StageZero.Services.Email;

namespace StageZero.Tests;

public class EmailOptionsTests
{
    private static EmailOptions Read(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>($"Email:{s.Key}", s.Value)))
            .Build();

        return EmailOptions.FromConfiguration(configuration);
    }

    [Fact]
    public void Empty_configuration_is_not_configured_and_names_what_is_missing()
    {
        var options = Read();

        Assert.False(options.IsConfigured);
        Assert.Equal(new[] { "Email__SmtpHost", "Email__FromEmail" }, options.MissingSettings);
        Assert.Contains("not configured", options.Describe());
    }

    [Fact]
    public void Shipped_appsettings_shape_with_blank_values_is_not_configured()
    {
        // appsettings.json ships every key as an empty string; that must still read as "off".
        var options = Read(
            ("SmtpHost", ""), ("SmtpPort", "587"), ("SmtpUsername", ""),
            ("SmtpPassword", ""), ("FromEmail", ""), ("FromName", "StageZero"));

        Assert.False(options.IsConfigured);
        Assert.Equal(EmailOptions.DefaultPort, options.SmtpPort);
        Assert.Null(options.SmtpUsername);
        Assert.Null(options.SmtpPassword);
    }

    [Fact]
    public void Host_and_from_address_are_enough_for_an_unauthenticated_relay()
    {
        var options = Read(("SmtpHost", "mail.lan"), ("FromEmail", "stagezero@lan"));

        Assert.True(options.IsConfigured);
        Assert.False(options.UsesAuthentication);
        Assert.True(options.UseStartTls, "STARTTLS is the default");
        Assert.Equal(EmailOptions.DefaultPort, options.SmtpPort);
    }

    [Theory]
    [InlineData("", EmailOptions.DefaultPort)]
    [InlineData("   ", EmailOptions.DefaultPort)]
    [InlineData("abc", EmailOptions.DefaultPort)]
    [InlineData("0", EmailOptions.DefaultPort)]
    [InlineData("2525", 2525)]
    [InlineData("465", 465)]
    public void Port_falls_back_to_587_unless_it_is_a_positive_number(string raw, int expected)
    {
        var options = Read(("SmtpHost", "h"), ("FromEmail", "f@h"), ("SmtpPort", raw));

        Assert.Equal(expected, options.SmtpPort);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("nope", true)]
    public void StartTls_is_on_unless_explicitly_false(string? raw, bool expected)
    {
        var options = Read(("SmtpHost", "h"), ("FromEmail", "f@h"), ("UseStartTls", raw));

        Assert.Equal(expected, options.UseStartTls);
    }

    [Fact]
    public void Values_are_trimmed_and_the_display_name_defaults()
    {
        var options = Read(
            ("SmtpHost", "  smtp.example.test "), ("FromEmail", " admin@example.test"),
            ("SmtpUsername", " user "), ("FromName", "  "));

        Assert.Equal("smtp.example.test", options.SmtpHost);
        Assert.Equal("admin@example.test", options.FromEmail);
        Assert.Equal("user", options.SmtpUsername);
        Assert.Equal("StageZero", options.FromName);
    }

    [Fact]
    public void Describe_never_leaks_the_password()
    {
        var options = Read(
            ("SmtpHost", "smtp.example.test"), ("SmtpPort", "587"), ("FromEmail", "admin@example.test"),
            ("SmtpUsername", "admin@example.test"), ("SmtpPassword", "hunter2-app-password"));

        var description = options.Describe();

        Assert.DoesNotContain("hunter2", description);
        Assert.Contains("smtp.example.test:587", description);
        Assert.Contains("STARTTLS", description);
        Assert.Contains("as admin@example.test", description);
    }
}
