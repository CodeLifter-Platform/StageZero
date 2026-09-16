using StageZero.DataAdapters.AccessServiceTokens;
using StageZero.DataAdapters.TunnelRoutes;
using StageZero.Models;
using StageZero.Services.Tunnel;

namespace StageZero.Services.Access;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Turns a route's access section into Cloudflare Access objects: one application per
/// hostname plus the reusable policies its mode calls for.
///
/// Every operation is idempotent — it looks an object up by ID and then by name before
/// creating it — so re-provisioning a hostname never leaves duplicate applications,
/// policies or service tokens behind.
/// </summary>
public interface IAccessProvisioningService
{
    /// <summary>
    /// Brings Cloudflare in line with <paramref name="route"/>'s access section and saves
    /// the resulting object IDs back onto the route.
    ///
    /// The returned result carries a newly minted service token exactly once, if one was
    /// created; its client secret is not stored anywhere and cannot be fetched again.
    /// </summary>
    Task<AccessProvisionResult> ProvisionAsync(ResolvedTunnelConfig config, TunnelRoute route);

    /// <summary>Reads the live Access configuration for a hostname, flagging any drift.</summary>
    Task<AccessStatus> GetStatusAsync(ResolvedTunnelConfig config, TunnelRoute route);

    /// <summary>Identity providers configured on the account, for the allowed-IdP picker.</summary>
    Task<List<AccessIdentityProvider>> ListIdentityProvidersAsync(ResolvedTunnelConfig config);

    /// <summary>Service tokens on the account, for attaching an existing one to a route.</summary>
    Task<List<AccessServiceTokenInfo>> ListServiceTokensAsync(ResolvedTunnelConfig config);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

/// <summary>What one provisioning run did. Returned to the caller, never logged wholesale.</summary>
public class AccessProvisionResult
{
    public string Hostname { get; set; } = string.Empty;
    public AccessMode Mode { get; set; }

    /// <summary>True when the hostname now sits behind an Access application.</summary>
    public bool ApplicationConfigured { get; set; }

    public string? ApplicationId { get; set; }
    public string? ApplicationAud { get; set; }
    public string? IdentityPolicyId { get; set; }
    public string? ServiceTokenPolicyId { get; set; }
    public string? ServiceTokenId { get; set; }
    public string? ServiceTokenName { get; set; }

    /// <summary>
    /// Set only on the run that minted a token. Holds the one and only copy of the client
    /// secret: show it once, hand it to the sink, then drop it. Never log or persist it.
    /// </summary>
    public MintedServiceToken? NewServiceToken { get; set; }

    /// <summary>True when switching to mode none removed an application a previous run made.</summary>
    public bool RemovedApplication { get; set; }
}

/// <summary>Live Access configuration for a hostname, as the status page shows it.</summary>
public class AccessStatus
{
    public string Hostname { get; set; } = string.Empty;

    /// <summary>The mode StageZero has stored for the route.</summary>
    public AccessMode ConfiguredMode { get; set; }

    /// <summary>The application Cloudflare currently has for the hostname, if any.</summary>
    public AccessApplication? Application { get; set; }

    /// <summary>The application's policies, resolved to their full definitions.</summary>
    public List<AccessPolicy> Policies { get; set; } = new();

    /// <summary>The service token attached to the route, if the mode uses one.</summary>
    public AccessServiceTokenInfo? ServiceToken { get; set; }

    /// <summary>True when StageZero minted the attached token, so teardown may remove it.</summary>
    public bool ServiceTokenCreatedByStageZero { get; set; }

    /// <summary>How many routes reference the attached token, including this one.</summary>
    public int ServiceTokenRouteCount { get; set; }

    /// <summary>Differences between what StageZero expects and what Cloudflare reports.</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>True when the hostname is reachable by anyone because no application covers it.</summary>
    public bool IsUnprotected => Application is null;
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class AccessProvisioningException : Exception
{
    public AccessProvisioningException(string message) : base(message) { }
    public AccessProvisioningException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class AccessProvisioningService : IAccessProvisioningService
{
    private readonly ILogger<AccessProvisioningService> _logger;
    private readonly ICloudflareAccessService _accessService;
    private readonly ITunnelRouteWriter _routeWriter;
    private readonly IAccessServiceTokenReader _tokenReader;
    private readonly IAccessServiceTokenWriter _tokenWriter;
    private readonly IAccessSecretSink _secretSink;

    public AccessProvisioningService(
        ILogger<AccessProvisioningService> logger,
        ICloudflareAccessService accessService,
        ITunnelRouteWriter routeWriter,
        IAccessServiceTokenReader tokenReader,
        IAccessServiceTokenWriter tokenWriter,
        IAccessSecretSink secretSink)
    {
        _logger = logger;
        _accessService = accessService;
        _routeWriter = routeWriter;
        _tokenReader = tokenReader;
        _tokenWriter = tokenWriter;
        _secretSink = secretSink;
    }

    // ───────────────────────────────────────────────────────────
    // PROVISIONING
    // ───────────────────────────────────────────────────────────

    public async Task<AccessProvisionResult> ProvisionAsync(ResolvedTunnelConfig config, TunnelRoute route)
    {
        var settings = route.Access;
        var hostname = route.DomainName;

        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new AccessProvisioningException(
                $"The Access settings for {hostname} are not usable: {string.Join(" ", errors)}");
        }

        var result = new AccessProvisionResult
        {
            Hostname = hostname,
            Mode = settings.Mode
        };

        if (settings.Mode == AccessMode.None)
        {
            // Opting out is explicit, and it has to actually remove the application —
            // otherwise the hostname would keep its old policy after being made public.
            result.RemovedApplication = await RemoveApplicationAndPoliciesAsync(config, route);
            await PersistAsync(route);
            return result;
        }

        await RequirePermissionsAsync(config, settings.Mode);

        // Mint or look up the token first: the policy references it by ID.
        MintedServiceToken? minted = null;
        if (settings.Mode.RequiresServiceTokenPolicy())
        {
            minted = await ResolveServiceTokenAsync(config, route);
        }

        var policyIds = await EnsurePoliciesAsync(config, route);
        var application = await EnsureApplicationAsync(config, route, policyIds);

        settings.ApplicationId = application.Id;
        settings.SyncedAt = DateTime.UtcNow;
        await PersistAsync(route);

        result.ApplicationConfigured = true;
        result.ApplicationId = application.Id;
        result.ApplicationAud = application.Aud;
        result.IdentityPolicyId = settings.IdentityPolicyId;
        result.ServiceTokenPolicyId = settings.ServiceTokenPolicyId;
        result.ServiceTokenId = settings.ServiceTokenId;
        result.ServiceTokenName = settings.ServiceTokenName;
        result.NewServiceToken = minted;

        _logger.LogInformation(
            "Access provisioned for {Hostname}: mode {Mode}, application {ApplicationId}",
            hostname, settings.Mode.ToWireValue(), application.Id);

        return result;
    }

    /// <summary>
    /// Fails before anything is created when the API token cannot manage Access. Access is
    /// account-scoped while StageZero's DNS work is zone-scoped, so a token that happily
    /// writes DNS records may still have no Access permissions at all.
    /// </summary>
    private async Task RequirePermissionsAsync(ResolvedTunnelConfig config, AccessMode mode)
    {
        var check = await _accessService.CheckPermissionsAsync(config.ApiToken, config.AccountId);

        if (!check.CanManageApplications
            || (mode.RequiresServiceTokenPolicy() && !check.SatisfiesServiceTokens))
        {
            throw new AccessProvisioningException(check.ToErrorMessage());
        }
    }

    // ───────────────────────────────────────────────────────────
    // SERVICE TOKENS
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the route's service token, minting one only when no existing token matches.
    /// Returns the minted token on the run that created it, and null on every later run —
    /// the client secret exists for that one moment.
    /// </summary>
    private async Task<MintedServiceToken?> ResolveServiceTokenAsync(
        ResolvedTunnelConfig config,
        TunnelRoute route)
    {
        var settings = route.Access;
        var existing = await _accessService.ListServiceTokensAsync(config.ApiToken, config.AccountId);

        // A stored ID wins, as long as Cloudflare still has that token.
        if (!string.IsNullOrWhiteSpace(settings.ServiceTokenId))
        {
            var byId = existing.FirstOrDefault(t =>
                string.Equals(t.Id, settings.ServiceTokenId, StringComparison.OrdinalIgnoreCase));

            if (byId is not null)
            {
                await RecordServiceTokenAsync(byId, createdByStageZero: false);
                settings.ServiceTokenName = byId.Name;
                settings.CreateServiceToken = false;
                return null;
            }

            _logger.LogWarning(
                "Service token {TokenId} for {Hostname} no longer exists in Cloudflare",
                settings.ServiceTokenId, route.DomainName);
            settings.ServiceTokenId = null;
        }

        // Then by name, which is what makes a re-run reuse the token it made last time
        // rather than minting a second one with the same name.
        if (!string.IsNullOrWhiteSpace(settings.ServiceTokenName))
        {
            var byName = existing.FirstOrDefault(t =>
                string.Equals(t.Name, settings.ServiceTokenName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (byName is not null)
            {
                await RecordServiceTokenAsync(byName, createdByStageZero: false);
                settings.ServiceTokenId = byName.Id;
                settings.CreateServiceToken = false;
                return null;
            }
        }

        if (!settings.CreateServiceToken)
        {
            throw new AccessProvisioningException(
                $"No Cloudflare service token matches '{settings.ServiceTokenName ?? settings.ServiceTokenId}' "
                + $"for {route.DomainName}. Pick an existing token or choose to create one.");
        }

        var minted = await _accessService.CreateServiceTokenAsync(
            config.ApiToken,
            config.AccountId,
            settings.ServiceTokenName!.Trim(),
            settings.ServiceTokenDuration);

        await RecordServiceTokenAsync(
            new AccessServiceTokenInfo
            {
                Id = minted.TokenId,
                Name = minted.Name,
                ClientId = minted.ClientId,
                Duration = minted.Duration,
                ExpiresAt = minted.ExpiresAt
            },
            createdByStageZero: true);

        settings.ServiceTokenId = minted.TokenId;
        settings.ServiceTokenName = minted.Name;

        // The token now exists; a re-run must reuse it rather than mint another.
        settings.CreateServiceToken = false;

        // Hand the secret to the configured sink before returning, so a deployment with a
        // vault keeps it even if the operator closes the one-time dialog without reading it.
        await _secretSink.StoreAsync(new AccessSecretContext
        {
            Hostname = route.DomainName,
            Token = minted
        });

        return minted;
    }

    /// <summary>Records what StageZero knows about a token. The client secret is never included.</summary>
    private async Task RecordServiceTokenAsync(AccessServiceTokenInfo token, bool createdByStageZero)
    {
        await _tokenWriter.UpsertAsync(new AccessServiceToken
        {
            CloudflareTokenId = token.Id,
            Name = token.Name,
            ClientId = token.ClientId,
            Duration = token.Duration,
            ExpiresAt = token.ExpiresAt,
            CreatedByStageZero = createdByStageZero
        });
    }

    // ───────────────────────────────────────────────────────────
    // POLICIES
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the policies the mode calls for exist and match the route, and removes the
    /// one the mode no longer needs. Returns the policy IDs in ascending precedence.
    /// </summary>
    private async Task<List<string>> EnsurePoliciesAsync(ResolvedTunnelConfig config, TunnelRoute route)
    {
        var settings = route.Access;
        var policyIds = new List<string>();

        // One listing covers both lookups below; the account policy list is not per-app.
        var accountPolicies = await _accessService.ListPoliciesAsync(config.ApiToken, config.AccountId);

        if (settings.Mode.RequiresIdentityPolicy())
        {
            settings.IdentityPolicyId = await EnsurePolicyAsync(
                config,
                settings.IdentityPolicyId,
                BuildIdentityPolicyRequest(route),
                accountPolicies);

            policyIds.Add(settings.IdentityPolicyId);
        }
        else if (!string.IsNullOrWhiteSpace(settings.IdentityPolicyId))
        {
            await _accessService.DeletePolicyAsync(config.ApiToken, config.AccountId, settings.IdentityPolicyId);
            settings.IdentityPolicyId = null;
        }

        if (settings.Mode.RequiresServiceTokenPolicy())
        {
            settings.ServiceTokenPolicyId = await EnsurePolicyAsync(
                config,
                settings.ServiceTokenPolicyId,
                BuildServiceTokenPolicyRequest(route),
                accountPolicies);

            policyIds.Add(settings.ServiceTokenPolicyId);
        }
        else if (!string.IsNullOrWhiteSpace(settings.ServiceTokenPolicyId))
        {
            await _accessService.DeletePolicyAsync(config.ApiToken, config.AccountId, settings.ServiceTokenPolicyId);
            settings.ServiceTokenPolicyId = null;
        }

        return policyIds;
    }

    /// <summary>
    /// Looks the policy up by stored ID, then by name, and only creates one when neither
    /// finds it. The name is derived from the hostname, so it is stable across runs.
    /// </summary>
    private async Task<string> EnsurePolicyAsync(
        ResolvedTunnelConfig config,
        string? knownPolicyId,
        AccessPolicyRequest request,
        List<AccessPolicy> accountPolicies)
    {
        if (!string.IsNullOrWhiteSpace(knownPolicyId))
        {
            var byId = await _accessService.GetPolicyAsync(config.ApiToken, config.AccountId, knownPolicyId);
            if (byId is not null)
            {
                var updated = await _accessService.UpdatePolicyAsync(
                    config.ApiToken, config.AccountId, byId.Id, request);
                return updated.Id;
            }

            _logger.LogWarning("Access policy {PolicyId} no longer exists; recreating it", knownPolicyId);
        }

        var byName = accountPolicies.FirstOrDefault(p =>
            string.Equals(p.Name, request.Name, StringComparison.OrdinalIgnoreCase));

        if (byName is not null)
        {
            var updated = await _accessService.UpdatePolicyAsync(
                config.ApiToken, config.AccountId, byName.Id, request);
            return updated.Id;
        }

        var created = await _accessService.CreatePolicyAsync(config.ApiToken, config.AccountId, request);
        return created.Id;
    }

    /// <summary>
    /// Builds the identity allow policy: every configured email and email domain becomes an
    /// include rule, and the rules are ORed, so any one of them grants access.
    /// </summary>
    public static AccessPolicyRequest BuildIdentityPolicyRequest(TunnelRoute route)
    {
        var settings = route.Access;
        var request = new AccessPolicyRequest
        {
            Name = RouteAccessSettings.IdentityPolicyNameFor(route.DomainName),
            Decision = AccessPolicyRequest.AllowDecision
        };

        foreach (var email in settings.AllowedEmailList)
        {
            request.Include.Add(AccessRuleFactory.Email(email));
        }

        foreach (var domain in settings.AllowedEmailDomainList)
        {
            request.Include.Add(AccessRuleFactory.EmailDomain(domain));
        }

        return request;
    }

    /// <summary>
    /// Builds the service token policy. The decision is non_identity, not allow: the caller
    /// is a machine presenting a token, not a human who logged in through an IdP.
    /// </summary>
    public static AccessPolicyRequest BuildServiceTokenPolicyRequest(TunnelRoute route)
    {
        var settings = route.Access;

        if (string.IsNullOrWhiteSpace(settings.ServiceTokenId))
        {
            throw new AccessProvisioningException(
                $"Cannot build the service token policy for {route.DomainName}: no token is attached yet.");
        }

        return new AccessPolicyRequest
        {
            Name = RouteAccessSettings.ServiceTokenPolicyNameFor(route.DomainName),
            Decision = AccessPolicyRequest.NonIdentityDecision,
            Include = { AccessRuleFactory.ServiceToken(settings.ServiceTokenId) }
        };
    }

    // ───────────────────────────────────────────────────────────
    // APPLICATION
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the application, or updates the one already covering the hostname. The
    /// lookup by domain is what keeps a re-run from stacking up duplicate applications
    /// when StageZero's stored ID has been lost.
    /// </summary>
    private async Task<AccessApplication> EnsureApplicationAsync(
        ResolvedTunnelConfig config,
        TunnelRoute route,
        List<string> policyIds)
    {
        var settings = route.Access;
        var request = BuildApplicationRequest(route, policyIds);

        AccessApplication? existing = null;

        if (!string.IsNullOrWhiteSpace(settings.ApplicationId))
        {
            existing = await _accessService.GetApplicationAsync(
                config.ApiToken, config.AccountId, settings.ApplicationId);

            if (existing is null)
            {
                _logger.LogWarning(
                    "Access application {ApplicationId} for {Hostname} no longer exists; recreating it",
                    settings.ApplicationId, route.DomainName);
            }
        }

        existing ??= await _accessService.FindApplicationByDomainAsync(
            config.ApiToken, config.AccountId, route.DomainName);

        return existing is null
            ? await _accessService.CreateApplicationAsync(config.ApiToken, config.AccountId, request)
            : await _accessService.UpdateApplicationAsync(
                config.ApiToken, config.AccountId, existing.Id, request);
    }

    public static AccessApplicationRequest BuildApplicationRequest(TunnelRoute route, List<string> policyIds)
    {
        var settings = route.Access;

        return new AccessApplicationRequest
        {
            Name = RouteAccessSettings.ApplicationNameFor(route.DomainName),
            Domain = route.DomainName,
            Type = "self_hosted",
            SessionDuration = string.IsNullOrWhiteSpace(settings.SessionDuration)
                ? RouteAccessSettings.DefaultSessionDuration
                : settings.SessionDuration.Trim(),
            AllowedIdpIds = settings.AllowedIdpIdList.ToList(),
            PolicyIds = policyIds
        };
    }

    /// <summary>
    /// Deletes the application and the reusable policies StageZero made for a hostname, and
    /// clears the stored IDs. Returns true when there was an application to remove.
    ///
    /// Reusable policies outlive the application that referenced them, so they have to be
    /// deleted explicitly or they accumulate on the account.
    /// </summary>
    private async Task<bool> RemoveApplicationAndPoliciesAsync(
        ResolvedTunnelConfig config,
        TunnelRoute route)
    {
        var settings = route.Access;

        var application = !string.IsNullOrWhiteSpace(settings.ApplicationId)
            ? await _accessService.GetApplicationAsync(config.ApiToken, config.AccountId, settings.ApplicationId)
            : null;

        application ??= await _accessService.FindApplicationByDomainAsync(
            config.ApiToken, config.AccountId, route.DomainName);

        var removed = false;

        if (application is not null)
        {
            await _accessService.DeleteApplicationAsync(config.ApiToken, config.AccountId, application.Id);
            removed = true;
        }

        foreach (var policyId in new[] { settings.IdentityPolicyId, settings.ServiceTokenPolicyId })
        {
            if (!string.IsNullOrWhiteSpace(policyId))
            {
                await _accessService.DeletePolicyAsync(config.ApiToken, config.AccountId, policyId);
            }
        }

        settings.ApplicationId = null;
        settings.IdentityPolicyId = null;
        settings.ServiceTokenPolicyId = null;
        settings.SyncedAt = null;

        return removed;
    }

    // ───────────────────────────────────────────────────────────
    // STATUS
    // ───────────────────────────────────────────────────────────

    public async Task<AccessStatus> GetStatusAsync(ResolvedTunnelConfig config, TunnelRoute route)
    {
        var settings = route.Access;
        var status = new AccessStatus
        {
            Hostname = route.DomainName,
            ConfiguredMode = settings.Mode
        };

        var application = !string.IsNullOrWhiteSpace(settings.ApplicationId)
            ? await _accessService.GetApplicationAsync(config.ApiToken, config.AccountId, settings.ApplicationId)
            : null;

        application ??= await _accessService.FindApplicationByDomainAsync(
            config.ApiToken, config.AccountId, route.DomainName);

        status.Application = application;

        if (settings.Mode.RequiresApplication() && application is null)
        {
            status.Warnings.Add(
                "No Access application covers this hostname, so it is reachable by anyone. "
                + "Save the route again to re-provision it.");
        }
        else if (settings.Mode == AccessMode.None && application is not null)
        {
            status.Warnings.Add(
                "Access mode is none but an Access application still covers this hostname.");
        }

        if (application is not null)
        {
            // The app carries policy links; the full rules live on the reusable policies.
            var accountPolicies = await _accessService.ListPoliciesAsync(config.ApiToken, config.AccountId);

            foreach (var link in application.Policies)
            {
                var policy = accountPolicies.FirstOrDefault(p => p.Id == link.Id);
                if (policy is not null)
                {
                    status.Policies.Add(policy);
                }
            }

            if (status.Policies.Count == 0)
            {
                status.Warnings.Add(
                    "The Access application has no policies attached, so every request is denied.");
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.ServiceTokenId))
        {
            var tokens = await _accessService.ListServiceTokensAsync(config.ApiToken, config.AccountId);
            status.ServiceToken = tokens.FirstOrDefault(t => t.Id == settings.ServiceTokenId);

            if (status.ServiceToken is null)
            {
                status.Warnings.Add(
                    $"Service token {settings.ServiceTokenId} is attached to this route but no longer "
                    + "exists in Cloudflare.");
            }

            var record = await _tokenReader.GetByCloudflareIdAsync(settings.ServiceTokenId);
            status.ServiceTokenCreatedByStageZero = record?.CreatedByStageZero ?? false;
            status.ServiceTokenRouteCount = await _tokenReader.CountRoutesUsingAsync(settings.ServiceTokenId);
        }

        return status;
    }

    // ───────────────────────────────────────────────────────────
    // ACCOUNT LOOKUPS
    // ───────────────────────────────────────────────────────────

    public async Task<List<AccessIdentityProvider>> ListIdentityProvidersAsync(ResolvedTunnelConfig config)
    {
        return await _accessService.ListIdentityProvidersAsync(config.ApiToken, config.AccountId);
    }

    public async Task<List<AccessServiceTokenInfo>> ListServiceTokensAsync(ResolvedTunnelConfig config)
    {
        return await _accessService.ListServiceTokensAsync(config.ApiToken, config.AccountId);
    }

    // ───────────────────────────────────────────────────────────
    // HELPERS
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the IDs provisioning just learned. Routes that have not been inserted yet are
    /// skipped — the caller writes the row first, so this only guards against misuse.
    /// </summary>
    private async Task PersistAsync(TunnelRoute route)
    {
        if (route.Id == 0)
        {
            return;
        }

        await _routeWriter.UpdateAsync(route);
    }
}
