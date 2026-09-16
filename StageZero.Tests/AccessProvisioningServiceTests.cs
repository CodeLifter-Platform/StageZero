using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Models;
using StageZero.Services.Access;
using StageZero.Services.Tunnel;
using StageZero.Tests.Fakes;

namespace StageZero.Tests;

/// <summary>
/// Provisioning behaviour against a fake Cloudflare Access API: policy construction per
/// mode, idempotent re-runs, and the rules teardown applies to service tokens.
/// </summary>
public class AccessProvisioningServiceTests
{
    private const string Hostname = "app.example.com";

    private readonly FakeCloudflareAccessService _cloudflare = new();
    private readonly FakeTunnelRouteWriter _routeWriter = new();
    private readonly FakeAccessServiceTokenStore _tokenStore = new();
    private readonly RecordingAccessSecretSink _secretSink = new();

    private readonly ResolvedTunnelConfig _config = new()
    {
        AccountId = "account-1",
        ZoneId = "zone-1",
        ApiToken = "token",
        TunnelId = "tunnel-1"
    };

    private AccessProvisioningService CreateService() =>
        new(NullLogger<AccessProvisioningService>.Instance,
            _cloudflare,
            _routeWriter,
            _tokenStore,
            _tokenStore,
            _secretSink);

    private static TunnelRoute Route(RouteAccessSettings access, int id = 1) => new()
    {
        Id = id,
        DomainName = Hostname,
        ForwardScheme = "http",
        ForwardHost = "localhost",
        ForwardPort = 8080,
        Access = access
    };

    // ═══════════════════════════════════════════════════════════
    // POLICY CONSTRUCTION
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Identity_policy_allows_every_configured_email_and_domain()
    {
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com\nops@example.com",
            AllowedEmailDomains = "example.net"
        });

        var request = AccessProvisioningService.BuildIdentityPolicyRequest(route);

        Assert.Equal("StageZero app.example.com identity", request.Name);
        Assert.Equal("allow", request.Decision);
        Assert.Equal(3, request.Include.Count);

        Assert.Equal("owner@example.com", EmailOf(request.Include[0]));
        Assert.Equal("ops@example.com", EmailOf(request.Include[1]));
        Assert.Equal("example.net", DomainOf(request.Include[2]));
    }

    [Fact]
    public void Service_token_policy_is_non_identity_and_names_the_token()
    {
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            ServiceTokenId = "tok-42"
        });

        var request = AccessProvisioningService.BuildServiceTokenPolicyRequest(route);

        Assert.Equal("StageZero app.example.com service token", request.Name);

        // A service token is matched by a machine presenting credentials, not by a human
        // who logged in, so the decision must not be the plain allow used for identity.
        Assert.Equal("non_identity", request.Decision);

        var rule = Assert.Single(request.Include);
        Assert.Equal("tok-42", TokenIdOf(rule));
    }

    [Fact]
    public void A_service_token_policy_cannot_be_built_before_a_token_is_attached()
    {
        var route = Route(new RouteAccessSettings { Mode = AccessMode.ServiceToken });

        Assert.Throws<AccessProvisioningException>(
            () => AccessProvisioningService.BuildServiceTokenPolicyRequest(route));
    }

    [Fact]
    public async Task Mode_identity_attaches_exactly_one_allow_policy()
    {
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        });

        await CreateService().ProvisionAsync(_config, route);

        var application = Assert.Single(_cloudflare.Applications);
        var link = Assert.Single(application.Policies);

        var policy = Assert.Single(_cloudflare.Policies);
        Assert.Equal(policy.Id, link.Id);
        Assert.Equal("allow", policy.Decision);
        Assert.Equal(1, link.Precedence);
    }

    [Fact]
    public async Task Mode_service_token_attaches_exactly_one_non_identity_policy()
    {
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            CreateServiceToken = true,
            ServiceTokenName = "ci"
        });

        await CreateService().ProvisionAsync(_config, route);

        var policy = Assert.Single(_cloudflare.Policies);
        Assert.Equal("non_identity", policy.Decision);
        Assert.Single(Assert.Single(_cloudflare.Applications).Policies);
    }

    [Fact]
    public async Task Mode_both_attaches_two_policies_in_ascending_precedence()
    {
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Both,
            AllowedEmails = "owner@example.com",
            CreateServiceToken = true,
            ServiceTokenName = "ci"
        });

        await CreateService().ProvisionAsync(_config, route);

        Assert.Equal(2, _cloudflare.Policies.Count);
        Assert.Contains(_cloudflare.Policies, p => p.Decision == "allow");
        Assert.Contains(_cloudflare.Policies, p => p.Decision == "non_identity");

        var links = Assert.Single(_cloudflare.Applications).Policies;
        Assert.Equal(new[] { 1, 2 }, links.Select(l => l.Precedence ?? 0).ToArray());
    }

    [Fact]
    public async Task Mode_none_creates_nothing()
    {
        var route = Route(new RouteAccessSettings { Mode = AccessMode.None });

        var result = await CreateService().ProvisionAsync(_config, route);

        Assert.False(result.ApplicationConfigured);
        Assert.Empty(_cloudflare.Applications);
        Assert.Empty(_cloudflare.Policies);
    }

    [Fact]
    public async Task Switching_to_mode_none_removes_the_application_and_its_policies()
    {
        var service = CreateService();
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        });

        await service.ProvisionAsync(_config, route);
        Assert.Single(_cloudflare.Applications);

        route.Access.Mode = AccessMode.None;
        var result = await service.ProvisionAsync(_config, route);

        Assert.True(result.RemovedApplication);
        Assert.Empty(_cloudflare.Applications);

        // Reusable policies outlive the app that referenced them, so they must go too.
        Assert.Empty(_cloudflare.Policies);
        Assert.Null(route.Access.ApplicationId);
        Assert.Null(route.Access.IdentityPolicyId);
    }

    [Fact]
    public async Task The_application_carries_the_configured_session_duration_and_identity_providers()
    {
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com",
            SessionDuration = "2h45m",
            AllowedIdpIds = "idp-1\nidp-2"
        });

        await CreateService().ProvisionAsync(_config, route);

        var application = Assert.Single(_cloudflare.Applications);
        Assert.Equal("2h45m", application.SessionDuration);
        Assert.Equal(new[] { "idp-1", "idp-2" }, application.AllowedIdpIds.ToArray());
        Assert.Equal("self_hosted", application.Type);
    }

    [Fact]
    public async Task No_identity_providers_means_every_provider_on_the_account()
    {
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        });

        await CreateService().ProvisionAsync(_config, route);

        // Cloudflare's own default when allowed_idps is omitted.
        Assert.Empty(Assert.Single(_cloudflare.Applications).AllowedIdpIds);
    }

    // ═══════════════════════════════════════════════════════════
    // VALIDATION AND PERMISSIONS
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Invalid_settings_fail_before_anything_is_created()
    {
        var route = Route(new RouteAccessSettings { Mode = AccessMode.Identity });

        await Assert.ThrowsAsync<AccessProvisioningException>(
            () => CreateService().ProvisionAsync(_config, route));

        Assert.Empty(_cloudflare.Calls);
    }

    [Fact]
    public async Task A_token_that_cannot_manage_access_fails_with_the_permissions_to_add()
    {
        _cloudflare.CanManageApplications = false;

        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        });

        var error = await Assert.ThrowsAsync<AccessProvisioningException>(
            () => CreateService().ProvisionAsync(_config, route));

        Assert.Contains("Access: Apps", error.Message);
        Assert.Empty(_cloudflare.Applications);
    }

    [Fact]
    public async Task Identity_mode_does_not_need_the_service_token_permission()
    {
        // Access is account-scoped as a whole, but the two permissions are granted
        // separately: an identity-only hostname must not be blocked by a missing one.
        _cloudflare.CanManageServiceTokens = false;

        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        });

        await CreateService().ProvisionAsync(_config, route);

        Assert.Single(_cloudflare.Applications);
    }

    [Fact]
    public async Task Service_token_mode_does_need_the_service_token_permission()
    {
        _cloudflare.CanManageServiceTokens = false;

        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            CreateServiceToken = true,
            ServiceTokenName = "ci"
        });

        var error = await Assert.ThrowsAsync<AccessProvisioningException>(
            () => CreateService().ProvisionAsync(_config, route));

        Assert.Contains("Access: Service Tokens", error.Message);
    }

    // ═══════════════════════════════════════════════════════════
    // IDEMPOTENCY
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Re_running_provisioning_updates_rather_than_duplicating()
    {
        var service = CreateService();
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Both,
            AllowedEmails = "owner@example.com",
            CreateServiceToken = true,
            ServiceTokenName = "ci"
        });

        var first = await service.ProvisionAsync(_config, route);
        var second = await service.ProvisionAsync(_config, route);

        Assert.Single(_cloudflare.Applications);
        Assert.Equal(2, _cloudflare.Policies.Count);
        Assert.Single(_cloudflare.ServiceTokens);

        Assert.Equal(first.ApplicationId, second.ApplicationId);
        Assert.Equal(first.IdentityPolicyId, second.IdentityPolicyId);
        Assert.Equal(first.ServiceTokenId, second.ServiceTokenId);

        Assert.Equal(1, _cloudflare.CountCalls(nameof(FakeCloudflareAccessService.CreateApplicationAsync)));
        Assert.Equal(1, _cloudflare.CountCalls(nameof(FakeCloudflareAccessService.CreateServiceTokenAsync)));
        Assert.Equal(2, _cloudflare.CountCalls(nameof(FakeCloudflareAccessService.CreatePolicyAsync)));
    }

    [Fact]
    public async Task An_application_created_outside_StageZero_is_adopted_rather_than_duplicated()
    {
        // StageZero has no stored ID, but Cloudflare already has an app for the hostname.
        _cloudflare.Applications.Add(new AccessApplication
        {
            Id = "existing-app",
            Name = "hand made",
            Domain = Hostname,
            Type = "self_hosted"
        });

        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        });

        var result = await CreateService().ProvisionAsync(_config, route);

        Assert.Equal("existing-app", result.ApplicationId);
        Assert.Single(_cloudflare.Applications);
        Assert.Equal(0, _cloudflare.CountCalls(nameof(FakeCloudflareAccessService.CreateApplicationAsync)));
    }

    [Fact]
    public async Task A_policy_that_already_exists_under_its_name_is_reused()
    {
        _cloudflare.Policies.Add(new AccessPolicy
        {
            Id = "existing-policy",
            Name = RouteAccessSettings.IdentityPolicyNameFor(Hostname),
            Decision = "allow"
        });

        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        });

        var result = await CreateService().ProvisionAsync(_config, route);

        Assert.Equal("existing-policy", result.IdentityPolicyId);
        Assert.Single(_cloudflare.Policies);
    }

    [Fact]
    public async Task A_stored_id_pointing_at_a_deleted_object_is_recreated_not_left_dangling()
    {
        var service = CreateService();
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        });

        await service.ProvisionAsync(_config, route);

        // Someone deleted both in the Cloudflare dashboard.
        _cloudflare.Applications.Clear();
        _cloudflare.Policies.Clear();

        var result = await service.ProvisionAsync(_config, route);

        Assert.Single(_cloudflare.Applications);
        Assert.Single(_cloudflare.Policies);
        Assert.Equal(_cloudflare.Applications[0].Id, result.ApplicationId);
    }

    [Fact]
    public async Task Changing_mode_from_both_to_identity_removes_the_service_token_policy()
    {
        var service = CreateService();
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Both,
            AllowedEmails = "owner@example.com",
            CreateServiceToken = true,
            ServiceTokenName = "ci"
        });

        await service.ProvisionAsync(_config, route);
        Assert.Equal(2, _cloudflare.Policies.Count);

        route.Access.Mode = AccessMode.Identity;
        await service.ProvisionAsync(_config, route);

        var policy = Assert.Single(_cloudflare.Policies);
        Assert.Equal("allow", policy.Decision);
        Assert.Null(route.Access.ServiceTokenPolicyId);
    }

    // ═══════════════════════════════════════════════════════════
    // SERVICE TOKEN MINTING
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task A_minted_token_is_returned_once_and_handed_to_the_secret_sink()
    {
        var service = CreateService();
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            CreateServiceToken = true,
            ServiceTokenName = "ci"
        });

        var first = await service.ProvisionAsync(_config, route);

        Assert.NotNull(first.NewServiceToken);
        Assert.False(string.IsNullOrWhiteSpace(first.NewServiceToken!.ClientSecret));

        var stored = Assert.Single(_secretSink.Stored);
        Assert.Equal(Hostname, stored.Hostname);
        Assert.Equal(first.NewServiceToken.ClientSecret, stored.Token.ClientSecret);

        // The secret exists for exactly one run; a re-run reuses the token silently.
        var second = await service.ProvisionAsync(_config, route);
        Assert.Null(second.NewServiceToken);
        Assert.Single(_secretSink.Stored);
    }

    [Fact]
    public async Task The_client_secret_is_never_recorded_in_the_token_store()
    {
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            CreateServiceToken = true,
            ServiceTokenName = "ci"
        });

        var result = await CreateService().ProvisionAsync(_config, route);

        var record = Assert.Single(_tokenStore.Tokens);
        var secret = result.NewServiceToken!.ClientSecret;

        // The record holds the client ID, which is not a secret, and nothing else from
        // the credential pair.
        Assert.Equal(result.NewServiceToken.ClientId, record.ClientId);
        Assert.DoesNotContain(secret, record.ClientId ?? string.Empty);
        Assert.DoesNotContain(secret, record.Name);
        Assert.True(record.CreatedByStageZero);
    }

    [Fact]
    public async Task An_existing_token_with_the_same_name_is_reused_instead_of_minted_again()
    {
        _cloudflare.ServiceTokens.Add(new AccessServiceTokenInfo
        {
            Id = "tok-existing",
            Name = "ci",
            ClientId = "tok-existing.access"
        });

        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            CreateServiceToken = true,
            ServiceTokenName = "ci"
        });

        var result = await CreateService().ProvisionAsync(_config, route);

        Assert.Null(result.NewServiceToken);
        Assert.Equal("tok-existing", result.ServiceTokenId);
        Assert.Single(_cloudflare.ServiceTokens);

        // Adopted, not minted — so teardown must never delete it.
        Assert.False(Assert.Single(_tokenStore.Tokens).CreatedByStageZero);
    }

    [Fact]
    public async Task Referencing_a_token_that_does_not_exist_fails_when_creation_was_not_asked_for()
    {
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            ServiceTokenName = "missing"
        });

        var error = await Assert.ThrowsAsync<AccessProvisioningException>(
            () => CreateService().ProvisionAsync(_config, route));

        Assert.Contains("No Cloudflare service token matches", error.Message);
    }

    [Fact]
    public async Task A_stored_token_id_that_no_longer_exists_falls_back_to_minting()
    {
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            ServiceTokenId = "tok-deleted",
            ServiceTokenName = "ci",
            CreateServiceToken = true
        });

        var result = await CreateService().ProvisionAsync(_config, route);

        Assert.NotNull(result.NewServiceToken);
        Assert.NotEqual("tok-deleted", result.ServiceTokenId);
    }

    // ═══════════════════════════════════════════════════════════
    // TEARDOWN
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Teardown_removes_the_application_and_its_policies()
    {
        var service = CreateService();
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        });

        await service.ProvisionAsync(_config, route);
        var result = await service.TeardownAsync(_config, route);

        Assert.True(result.ApplicationRemoved);
        Assert.Equal(1, result.PoliciesRemoved);
        Assert.Empty(_cloudflare.Applications);
        Assert.Empty(_cloudflare.Policies);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Teardown_deletes_a_token_StageZero_minted_when_nothing_else_uses_it()
    {
        var service = CreateService();
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            CreateServiceToken = true,
            ServiceTokenName = "ci"
        });

        await service.ProvisionAsync(_config, route);
        var result = await service.TeardownAsync(_config, route);

        Assert.True(result.ServiceTokenRemoved);
        Assert.Null(result.ServiceTokenRetainedReason);
        Assert.Empty(_cloudflare.ServiceTokens);
        Assert.Empty(_tokenStore.Tokens);
    }

    [Fact]
    public async Task Teardown_keeps_a_token_StageZero_did_not_create_and_says_why()
    {
        _cloudflare.ServiceTokens.Add(new AccessServiceTokenInfo { Id = "tok-theirs", Name = "shared-ci" });

        var service = CreateService();
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            ServiceTokenName = "shared-ci"
        });

        await service.ProvisionAsync(_config, route);
        var result = await service.TeardownAsync(_config, route);

        Assert.False(result.ServiceTokenRemoved);
        Assert.Contains("StageZero did not create it", result.ServiceTokenRetainedReason);
        Assert.Single(_cloudflare.ServiceTokens);
    }

    [Fact]
    public async Task Teardown_keeps_a_minted_token_that_another_route_still_references()
    {
        var service = CreateService();
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.ServiceToken,
            CreateServiceToken = true,
            ServiceTokenName = "ci"
        });

        await service.ProvisionAsync(_config, route);

        // A second hostname was pointed at the same token.
        _tokenStore.OtherRouteCounts[route.Access.ServiceTokenId!] = 1;

        var result = await service.TeardownAsync(_config, route);

        Assert.False(result.ServiceTokenRemoved);
        Assert.Contains("1 other route", result.ServiceTokenRetainedReason);
        Assert.Single(_cloudflare.ServiceTokens);
        Assert.Single(_tokenStore.Tokens);
    }

    [Fact]
    public async Task Teardown_with_no_token_attached_reports_nothing_about_tokens()
    {
        var service = CreateService();
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        });

        await service.ProvisionAsync(_config, route);
        var result = await service.TeardownAsync(_config, route);

        Assert.False(result.ServiceTokenRemoved);
        Assert.Null(result.ServiceTokenRetainedReason);
    }

    [Fact]
    public async Task A_failure_while_removing_the_application_is_reported_not_thrown()
    {
        var service = CreateService();
        var route = Route(new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            AllowedEmails = "owner@example.com"
        });

        await service.ProvisionAsync(_config, route);
        _cloudflare.FailOnCall = nameof(FakeCloudflareAccessService.DeleteApplicationAsync);

        // By the time teardown runs the hostname is already unreachable, and a leftover
        // application denies traffic rather than allowing it — so this must not throw.
        var result = await service.TeardownAsync(_config, route);

        Assert.False(result.ApplicationRemoved);
        Assert.Contains(result.Warnings, w => w.Contains("could not be removed"));
    }

    // ═══════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════

    private static string? EmailOf(Dictionary<string, object> rule) => Nested(rule, "email", "email");

    private static string? DomainOf(Dictionary<string, object> rule) => Nested(rule, "email_domain", "domain");

    private static string? TokenIdOf(Dictionary<string, object> rule) => Nested(rule, "service_token", "token_id");

    private static string? Nested(Dictionary<string, object> rule, string outer, string inner)
    {
        Assert.True(rule.ContainsKey(outer), $"Expected rule key '{outer}', got: {string.Join(",", rule.Keys)}");
        var nested = Assert.IsType<Dictionary<string, object>>(rule[outer]);
        return nested[inner] as string;
    }
}
