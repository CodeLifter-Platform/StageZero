using StageZero.Models;

namespace StageZero.Tests;

/// <summary>
/// Parsing and validation of a route's access section. These run before any Cloudflare call
/// is made, so they are what stops a typo becoming a half-provisioned hostname.
/// </summary>
public class RouteAccessSettingsTests
{
    // ── Mode parsing ────────────────────────────────────────────

    [Theory]
    [InlineData("none", AccessMode.None)]
    [InlineData("identity", AccessMode.Identity)]
    [InlineData("service_token", AccessMode.ServiceToken)]
    [InlineData("both", AccessMode.Both)]
    [InlineData("  Identity  ", AccessMode.Identity)]
    [InlineData("BOTH", AccessMode.Both)]
    public void TryParse_accepts_the_four_documented_modes(string value, AccessMode expected)
    {
        Assert.True(AccessModes.TryParse(value, out var mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData("service-token")]
    [InlineData("servicetoken")]
    [InlineData("all")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_rejects_anything_else(string? value)
    {
        Assert.False(AccessModes.TryParse(value, out var mode));
        Assert.Equal(AccessMode.None, mode);
    }

    [Theory]
    [InlineData(AccessMode.None, "none")]
    [InlineData(AccessMode.Identity, "identity")]
    [InlineData(AccessMode.ServiceToken, "service_token")]
    [InlineData(AccessMode.Both, "both")]
    public void Modes_round_trip_through_their_stored_value(AccessMode mode, string expected)
    {
        var wire = mode.ToWireValue();

        Assert.Equal(expected, wire);
        Assert.Equal(mode, AccessModes.Parse(wire));
    }

    [Theory]
    [InlineData(AccessMode.Identity, true, false)]
    [InlineData(AccessMode.ServiceToken, false, true)]
    [InlineData(AccessMode.Both, true, true)]
    [InlineData(AccessMode.None, false, false)]
    public void Each_mode_declares_the_policies_it_needs(
        AccessMode mode, bool needsIdentity, bool needsServiceToken)
    {
        Assert.Equal(needsIdentity, mode.RequiresIdentityPolicy());
        Assert.Equal(needsServiceToken, mode.RequiresServiceTokenPolicy());
        Assert.Equal(mode != AccessMode.None, mode.RequiresApplication());
    }

    // ── List parsing ────────────────────────────────────────────

    [Fact]
    public void Lists_split_on_newlines_commas_and_semicolons()
    {
        var settings = new RouteAccessSettings
        {
            AllowedEmails = "a@example.com\nb@example.com, c@example.com; d@example.com"
        };

        Assert.Equal(
            new[] { "a@example.com", "b@example.com", "c@example.com", "d@example.com" },
            settings.AllowedEmailList);
    }

    [Fact]
    public void Lists_trim_lowercase_and_drop_duplicates_and_blanks()
    {
        var settings = new RouteAccessSettings
        {
            AllowedEmails = "  Owner@Example.com \n\n owner@example.com \n , \n Second@Example.com"
        };

        Assert.Equal(new[] { "owner@example.com", "second@example.com" }, settings.AllowedEmailList);
    }

    [Fact]
    public void An_empty_list_field_yields_no_entries()
    {
        var settings = new RouteAccessSettings { AllowedEmails = "   " };

        Assert.Empty(settings.AllowedEmailList);
        Assert.Empty(settings.AllowedEmailDomainList);
        Assert.Empty(settings.AllowedIdpIdList);
    }

    // ── Validation: none ────────────────────────────────────────

    [Fact]
    public void Mode_none_needs_nothing_else()
    {
        var settings = new RouteAccessSettings { Mode = AccessMode.None };

        Assert.Empty(settings.Validate());
    }

    // ── Validation: identity ────────────────────────────────────

    [Fact]
    public void Identity_mode_accepts_emails_domains_or_both()
    {
        var byEmail = new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        };

        var byDomain = new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmailDomains = "example.com"
        };

        Assert.Empty(byEmail.Validate());
        Assert.Empty(byDomain.Validate());
    }

    [Fact]
    public void Identity_mode_with_no_principals_is_rejected()
    {
        var settings = new RouteAccessSettings { Mode = AccessMode.Identity };

        var errors = settings.Validate();

        // An allow policy with no include rules matches nobody, which would lock the
        // hostname down completely rather than protecting it.
        Assert.Contains(errors, e => e.Contains("at least one allowed email"));
    }

    [Fact]
    public void Malformed_emails_are_rejected()
    {
        var settings = new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "not-an-email"
        };

        Assert.Contains(settings.Validate(), e => e.Contains("not a valid email address"));
    }

    [Fact]
    public void An_email_address_in_the_domains_field_is_called_out_specifically()
    {
        var settings = new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmailDomains = "owner@example.com"
        };

        Assert.Contains(settings.Validate(), e => e.Contains("looks like an email address"));
    }

    [Fact]
    public void Malformed_domains_are_rejected()
    {
        var settings = new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmailDomains = "example"
        };

        Assert.Contains(settings.Validate(), e => e.Contains("not a valid email domain"));
    }

    // ── Validation: service token ───────────────────────────────

    [Fact]
    public void Service_token_mode_accepts_an_existing_token_by_name_or_id()
    {
        var byName = new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            ServiceTokenName = "ci"
        };

        var byId = new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            ServiceTokenId = "tok-1"
        };

        Assert.Empty(byName.Validate());
        Assert.Empty(byId.Validate());
    }

    [Fact]
    public void Service_token_mode_with_no_token_at_all_is_rejected()
    {
        var settings = new RouteAccessSettings { Mode = AccessMode.ServiceToken };

        Assert.Contains(settings.Validate(), e => e.Contains("existing token's name or ID"));
    }

    [Fact]
    public void Creating_a_token_without_a_name_is_rejected()
    {
        var settings = new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            CreateServiceToken = true
        };

        Assert.Contains(settings.Validate(), e => e.Contains("needs a name"));
    }

    [Fact]
    public void Mode_both_validates_the_identity_and_service_token_halves_together()
    {
        var settings = new RouteAccessSettings { Mode = AccessMode.Both };

        var errors = settings.Validate();

        Assert.Contains(errors, e => e.Contains("at least one allowed email"));
        Assert.Contains(errors, e => e.Contains("existing token's name or ID"));
    }

    // ── Validation: durations ───────────────────────────────────

    [Theory]
    [InlineData("24h")]
    [InlineData("30m")]
    [InlineData("2h45m")]
    [InlineData("300ms")]
    public void Valid_session_durations_pass(string duration)
    {
        var settings = new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com",
            SessionDuration = duration
        };

        Assert.Empty(settings.Validate());
    }

    [Theory]
    [InlineData("24 hours")]
    [InlineData("forever")]
    [InlineData("1d")]
    public void Invalid_session_durations_are_rejected(string duration)
    {
        var settings = new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com",
            SessionDuration = duration
        };

        Assert.Contains(settings.Validate(), e => e.Contains("not a Cloudflare duration"));
    }

    [Fact]
    public void Forever_is_valid_for_a_token_lifetime_though_not_for_a_session()
    {
        var settings = new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            ServiceTokenName = "ci",
            ServiceTokenDuration = "forever"
        };

        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void An_invalid_token_lifetime_is_rejected()
    {
        var settings = new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            ServiceTokenName = "ci",
            ServiceTokenDuration = "one year"
        };

        Assert.Contains(settings.Validate(), e => e.Contains("Service token duration"));
    }

    // ── Naming ──────────────────────────────────────────────────

    [Fact]
    public void Policy_names_are_derived_from_the_hostname_so_they_are_stable_across_runs()
    {
        // Idempotency leans on these names: a re-run that has lost the stored policy ID
        // finds its policy by name instead of creating a second one.
        Assert.Equal("StageZero app.example.com identity",
            RouteAccessSettings.IdentityPolicyNameFor("app.example.com"));

        Assert.Equal("StageZero app.example.com service token",
            RouteAccessSettings.ServiceTokenPolicyNameFor("app.example.com"));

        Assert.Equal("app.example.com", RouteAccessSettings.ApplicationNameFor("app.example.com"));
    }
}
