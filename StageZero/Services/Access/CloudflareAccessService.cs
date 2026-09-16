using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StageZero.Services.Access;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raw HTTP client for the Cloudflare Access (Zero Trust) API. Access objects are
/// account-scoped, unlike the zone-scoped DNS records StageZero also writes.
///
/// Policies here are the current account-level reusable kind
/// (<c>/accounts/{id}/access/policies</c>). The older app-scoped policy endpoints are
/// legacy and cannot be attached to newly created applications.
/// </summary>
public interface ICloudflareAccessService
{
    /// <summary>
    /// Probes the Access endpoints so a token missing the account-scoped Access
    /// permissions fails before StageZero starts changing anything.
    /// </summary>
    Task<AccessPermissionCheck> CheckPermissionsAsync(string apiToken, string accountId);

    // ── Applications ────────────────────────────────────────────

    /// <summary>Exact-match lookup used for idempotency. Null when no app covers the hostname.</summary>
    Task<AccessApplication?> FindApplicationByDomainAsync(string apiToken, string accountId, string hostname);

    Task<AccessApplication?> GetApplicationAsync(string apiToken, string accountId, string applicationId);

    Task<AccessApplication> CreateApplicationAsync(
        string apiToken, string accountId, AccessApplicationRequest request);

    Task<AccessApplication> UpdateApplicationAsync(
        string apiToken, string accountId, string applicationId, AccessApplicationRequest request);

    /// <summary>Deletes the application. No-op if Cloudflare no longer has it.</summary>
    Task DeleteApplicationAsync(string apiToken, string accountId, string applicationId);

    // ── Reusable policies ───────────────────────────────────────

    Task<List<AccessPolicy>> ListPoliciesAsync(string apiToken, string accountId);

    Task<AccessPolicy?> GetPolicyAsync(string apiToken, string accountId, string policyId);

    Task<AccessPolicy> CreatePolicyAsync(string apiToken, string accountId, AccessPolicyRequest request);

    Task<AccessPolicy> UpdatePolicyAsync(
        string apiToken, string accountId, string policyId, AccessPolicyRequest request);

    /// <summary>Deletes the reusable policy. No-op if Cloudflare no longer has it.</summary>
    Task DeletePolicyAsync(string apiToken, string accountId, string policyId);

    // ── Service tokens ──────────────────────────────────────────

    Task<List<AccessServiceTokenInfo>> ListServiceTokensAsync(string apiToken, string accountId);

    /// <summary>
    /// Mints a service token. The client secret comes back only on this call and is never
    /// returned again, so the result must reach its destination before it is discarded.
    /// </summary>
    Task<MintedServiceToken> CreateServiceTokenAsync(
        string apiToken, string accountId, string name, string? duration);

    /// <summary>Deletes the service token. No-op if Cloudflare no longer has it.</summary>
    Task DeleteServiceTokenAsync(string apiToken, string accountId, string tokenId);

    // ── Identity providers ──────────────────────────────────────

    /// <summary>The IdPs configured on the account, for the "allowed identity providers" picker.</summary>
    Task<List<AccessIdentityProvider>> ListIdentityProvidersAsync(string apiToken, string accountId);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

public class AccessApplication
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? SessionDuration { get; set; }

    /// <summary>Audience tag, shown on the status page so a JWT can be traced to this app.</summary>
    public string? Aud { get; set; }

    public List<string> AllowedIdpIds { get; set; } = new();

    /// <summary>Policies attached to the app, in the order Cloudflare returned them.</summary>
    public List<AccessApplicationPolicyLink> Policies { get; set; } = new();
}

public class AccessApplicationPolicyLink
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Decision { get; set; }
    public int? Precedence { get; set; }
}

public class AccessPolicy
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;

    /// <summary>Include rules as Cloudflare returned them, summarised for display.</summary>
    public List<string> IncludeSummary { get; set; } = new();
}

public class AccessServiceTokenInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ClientId { get; set; }
    public string? Duration { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// A newly minted service token, including the one-and-only copy of its client secret.
/// Never log, serialise or persist <see cref="ClientSecret"/>.
/// </summary>
public class MintedServiceToken
{
    public string TokenId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string? Duration { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class AccessIdentityProvider
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

/// <summary>Result of probing whether the saved API token can manage Access at all.</summary>
public class AccessPermissionCheck
{
    public bool CanManageApplications { get; set; }
    public bool CanManageServiceTokens { get; set; }

    /// <summary>Permission names to add to the token, phrased as Cloudflare's UI shows them.</summary>
    public List<string> MissingPermissions { get; set; } = new();

    public bool IsSatisfied => MissingPermissions.Count == 0;

    /// <summary>Whether the token can do everything the given mode needs.</summary>
    public bool SatisfiesServiceTokens => CanManageServiceTokens;

    public string ToErrorMessage() =>
        "The saved Cloudflare API token cannot manage Access. Add these account-scoped "
        + $"permissions and save the token again: {string.Join("; ", MissingPermissions)}.";
}

/// <summary>Body for creating or replacing a self-hosted Access application.</summary>
public class AccessApplicationRequest
{
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Type { get; set; } = "self_hosted";
    public string? SessionDuration { get; set; }

    /// <summary>Empty means every IdP configured on the account, which is Cloudflare's default.</summary>
    public List<string> AllowedIdpIds { get; set; } = new();

    /// <summary>Reusable policy IDs, in ascending order of precedence.</summary>
    public List<string> PolicyIds { get; set; } = new();
}

/// <summary>Body for creating or replacing a reusable Access policy.</summary>
public class AccessPolicyRequest
{
    /// <summary>Allows anyone matching an identity rule.</summary>
    public const string AllowDecision = "allow";

    /// <summary>
    /// Service auth. A non-identity policy is matched by a service token rather than a
    /// logged-in human, so it must not use the plain allow decision.
    /// </summary>
    public const string NonIdentityDecision = "non_identity";

    public string Name { get; set; } = string.Empty;
    public string Decision { get; set; } = AllowDecision;

    /// <summary>Rules ORed together — a caller matching any one of them is let in.</summary>
    public List<Dictionary<string, object>> Include { get; set; } = new();
}

/// <summary>
/// Builds Cloudflare's include-rule objects. Kept as dictionaries rather than anonymous
/// types so policy construction can be asserted on directly in tests.
/// </summary>
public static class AccessRuleFactory
{
    public static Dictionary<string, object> Email(string email) =>
        new() { ["email"] = new Dictionary<string, object> { ["email"] = email } };

    public static Dictionary<string, object> EmailDomain(string domain) =>
        new() { ["email_domain"] = new Dictionary<string, object> { ["domain"] = domain } };

    public static Dictionary<string, object> ServiceToken(string tokenId) =>
        new() { ["service_token"] = new Dictionary<string, object> { ["token_id"] = tokenId } };
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class CloudflareAccessServiceException : Exception
{
    public CloudflareAccessServiceException(string message) : base(message) { }
    public CloudflareAccessServiceException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class CloudflareAccessService : ICloudflareAccessService
{
    private readonly ILogger<CloudflareAccessService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private const string CLOUDFLARE_API_BASE = "https://api.cloudflare.com/client/v4";

    /// <summary>Page size for the list endpoints, which paginate rather than return everything.</summary>
    private const int PAGE_SIZE = 100;

    /// <summary>Stops a bad cursor from looping forever. 50 pages is 5,000 objects.</summary>
    private const int MAX_PAGES = 50;

    public CloudflareAccessService(
        ILogger<CloudflareAccessService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    // ───────────────────────────────────────────────────────────
    // PERMISSIONS
    // ───────────────────────────────────────────────────────────

    public async Task<AccessPermissionCheck> CheckPermissionsAsync(string apiToken, string accountId)
    {
        var check = new AccessPermissionCheck();

        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);

            check.CanManageApplications = await CanReachAsync(
                httpClient, $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/apps?per_page=1");

            check.CanManageServiceTokens = await CanReachAsync(
                httpClient, $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/service_tokens?per_page=1");

            if (!check.CanManageApplications)
            {
                check.MissingPermissions.Add("Access: Apps Read and Access: Apps Edit");
            }

            if (!check.CanManageServiceTokens)
            {
                check.MissingPermissions.Add("Access: Service Tokens Read and Access: Service Tokens Edit");
            }

            return check;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Cloudflare Access permissions for account {AccountId}", accountId);
            throw new CloudflareAccessServiceException("Could not check Access permissions", ex);
        }
    }

    /// <summary>
    /// A 403 means the token lacks the permission. Anything else that is not a success —
    /// a network fault, a 500 — is not evidence either way, so it is reported as reachable
    /// and the real call surfaces the error instead.
    /// </summary>
    private async Task<bool> CanReachAsync(HttpClient httpClient, string url)
    {
        var response = await httpClient.GetAsync(url);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return false;
        }

        return true;
    }

    // ───────────────────────────────────────────────────────────
    // APPLICATIONS
    // ───────────────────────────────────────────────────────────

    public async Task<AccessApplication?> FindApplicationByDomainAsync(
        string apiToken,
        string accountId,
        string hostname)
    {
        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.GetAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/apps"
                + $"?domain={Uri.EscapeDataString(hostname)}&exact=true&per_page={PAGE_SIZE}");

            var root = await ReadResultAsync(response, $"look up the Access app for {hostname}");

            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            // exact=true still matches on the app's other destinations, so confirm the
            // primary domain before treating this as "the app for this hostname".
            foreach (var app in root.EnumerateArray())
            {
                var parsed = ParseApplication(app);
                if (string.Equals(parsed.Domain, hostname, StringComparison.OrdinalIgnoreCase))
                {
                    return parsed;
                }
            }

            return null;
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up the Access application for {Hostname}", hostname);
            throw new CloudflareAccessServiceException(
                $"Could not look up the Access application for {hostname}", ex);
        }
    }

    public async Task<AccessApplication?> GetApplicationAsync(
        string apiToken,
        string accountId,
        string applicationId)
    {
        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.GetAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/apps/{applicationId}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            var root = await ReadResultAsync(response, "get Access application");
            return root.ValueKind == JsonValueKind.Object ? ParseApplication(root) : null;
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Access application {ApplicationId}", applicationId);
            throw new CloudflareAccessServiceException("Could not get the Access application", ex);
        }
    }

    public async Task<AccessApplication> CreateApplicationAsync(
        string apiToken,
        string accountId,
        AccessApplicationRequest request)
    {
        try
        {
            _logger.LogInformation("Creating Access application for {Domain}", request.Domain);

            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.PostAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/apps",
                JsonBody(BuildApplicationPayload(request)));

            var root = await ReadResultAsync(response, $"create the Access app for {request.Domain}");
            var created = ParseApplication(root);

            _logger.LogInformation("Created Access application {ApplicationId} for {Domain}",
                created.Id, created.Domain);

            return created;
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating the Access application for {Domain}", request.Domain);
            throw new CloudflareAccessServiceException(
                $"Could not create the Access application for {request.Domain}", ex);
        }
    }

    public async Task<AccessApplication> UpdateApplicationAsync(
        string apiToken,
        string accountId,
        string applicationId,
        AccessApplicationRequest request)
    {
        try
        {
            _logger.LogInformation("Updating Access application {ApplicationId} for {Domain}",
                applicationId, request.Domain);

            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.PutAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/apps/{applicationId}",
                JsonBody(BuildApplicationPayload(request)));

            var root = await ReadResultAsync(response, $"update the Access app for {request.Domain}");
            return ParseApplication(root);
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Access application {ApplicationId}", applicationId);
            throw new CloudflareAccessServiceException(
                $"Could not update the Access application for {request.Domain}", ex);
        }
    }

    public async Task DeleteApplicationAsync(string apiToken, string accountId, string applicationId)
    {
        try
        {
            _logger.LogInformation("Deleting Access application {ApplicationId}", applicationId);

            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.DeleteAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/apps/{applicationId}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Access application {ApplicationId} was already gone", applicationId);
                return;
            }

            await ReadResultAsync(response, "delete Access application");
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Access application {ApplicationId}", applicationId);
            throw new CloudflareAccessServiceException("Could not delete the Access application", ex);
        }
    }

    // ───────────────────────────────────────────────────────────
    // REUSABLE POLICIES
    // ───────────────────────────────────────────────────────────

    public async Task<List<AccessPolicy>> ListPoliciesAsync(string apiToken, string accountId)
    {
        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);
            var policies = new List<AccessPolicy>();

            for (var page = 1; page <= MAX_PAGES; page++)
            {
                var response = await httpClient.GetAsync(
                    $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/policies"
                    + $"?page={page}&per_page={PAGE_SIZE}");

                var root = await ReadResultAsync(response, "list Access policies");

                if (root.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                var pageCount = 0;
                foreach (var policy in root.EnumerateArray())
                {
                    policies.Add(ParsePolicy(policy));
                    pageCount++;
                }

                if (pageCount < PAGE_SIZE)
                {
                    break;
                }
            }

            return policies;
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Access policies");
            throw new CloudflareAccessServiceException("Could not list Access policies", ex);
        }
    }

    public async Task<AccessPolicy?> GetPolicyAsync(string apiToken, string accountId, string policyId)
    {
        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.GetAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/policies/{policyId}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            var root = await ReadResultAsync(response, "get Access policy");
            return root.ValueKind == JsonValueKind.Object ? ParsePolicy(root) : null;
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Access policy {PolicyId}", policyId);
            throw new CloudflareAccessServiceException("Could not get the Access policy", ex);
        }
    }

    public async Task<AccessPolicy> CreatePolicyAsync(
        string apiToken,
        string accountId,
        AccessPolicyRequest request)
    {
        try
        {
            _logger.LogInformation("Creating Access policy {PolicyName} ({Decision})",
                request.Name, request.Decision);

            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.PostAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/policies",
                JsonBody(BuildPolicyPayload(request)));

            var root = await ReadResultAsync(response, $"create the Access policy {request.Name}");
            return ParsePolicy(root);
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Access policy {PolicyName}", request.Name);
            throw new CloudflareAccessServiceException(
                $"Could not create the Access policy {request.Name}", ex);
        }
    }

    public async Task<AccessPolicy> UpdatePolicyAsync(
        string apiToken,
        string accountId,
        string policyId,
        AccessPolicyRequest request)
    {
        try
        {
            _logger.LogInformation("Updating Access policy {PolicyId} ({PolicyName})", policyId, request.Name);

            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.PutAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/policies/{policyId}",
                JsonBody(BuildPolicyPayload(request)));

            var root = await ReadResultAsync(response, $"update the Access policy {request.Name}");
            return ParsePolicy(root);
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Access policy {PolicyId}", policyId);
            throw new CloudflareAccessServiceException(
                $"Could not update the Access policy {request.Name}", ex);
        }
    }

    public async Task DeletePolicyAsync(string apiToken, string accountId, string policyId)
    {
        try
        {
            _logger.LogInformation("Deleting Access policy {PolicyId}", policyId);

            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.DeleteAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/policies/{policyId}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Access policy {PolicyId} was already gone", policyId);
                return;
            }

            await ReadResultAsync(response, "delete Access policy");
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Access policy {PolicyId}", policyId);
            throw new CloudflareAccessServiceException("Could not delete the Access policy", ex);
        }
    }

    // ───────────────────────────────────────────────────────────
    // SERVICE TOKENS
    // ───────────────────────────────────────────────────────────

    public async Task<List<AccessServiceTokenInfo>> ListServiceTokensAsync(string apiToken, string accountId)
    {
        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);
            var tokens = new List<AccessServiceTokenInfo>();

            for (var page = 1; page <= MAX_PAGES; page++)
            {
                var response = await httpClient.GetAsync(
                    $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/service_tokens"
                    + $"?page={page}&per_page={PAGE_SIZE}");

                var root = await ReadResultAsync(response, "list Access service tokens");

                if (root.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                var pageCount = 0;
                foreach (var token in root.EnumerateArray())
                {
                    tokens.Add(ParseServiceToken(token));
                    pageCount++;
                }

                if (pageCount < PAGE_SIZE)
                {
                    break;
                }
            }

            return tokens;
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Access service tokens");
            throw new CloudflareAccessServiceException("Could not list Access service tokens", ex);
        }
    }

    public async Task<MintedServiceToken> CreateServiceTokenAsync(
        string apiToken,
        string accountId,
        string name,
        string? duration)
    {
        try
        {
            // The token name is safe to log; nothing below ever logs the response body,
            // because it carries the only copy of the client secret.
            _logger.LogInformation("Creating Access service token {TokenName}", name);

            var payload = new Dictionary<string, object> { ["name"] = name };
            if (!string.IsNullOrWhiteSpace(duration))
            {
                payload["duration"] = duration.Trim();
            }

            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.PostAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/service_tokens",
                JsonBody(payload));

            var root = await ReadResultAsync(response, $"create the service token {name}");

            var minted = new MintedServiceToken
            {
                TokenId = ReadString(root, "id") ?? string.Empty,
                Name = ReadString(root, "name") ?? name,
                ClientId = ReadString(root, "client_id") ?? string.Empty,
                ClientSecret = ReadString(root, "client_secret") ?? string.Empty,
                Duration = ReadString(root, "duration"),
                ExpiresAt = ReadDateTime(root, "expires_at")
            };

            if (string.IsNullOrWhiteSpace(minted.ClientSecret))
            {
                throw new CloudflareAccessServiceException(
                    $"Cloudflare created the service token {name} but returned no client secret. "
                    + "The secret cannot be retrieved later — delete the token and try again.");
            }

            _logger.LogInformation("Created Access service token {TokenName} ({TokenId})",
                minted.Name, minted.TokenId);

            return minted;
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Access service token {TokenName}", name);
            throw new CloudflareAccessServiceException($"Could not create the service token {name}", ex);
        }
    }

    public async Task DeleteServiceTokenAsync(string apiToken, string accountId, string tokenId)
    {
        try
        {
            _logger.LogInformation("Deleting Access service token {TokenId}", tokenId);

            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.DeleteAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/service_tokens/{tokenId}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Access service token {TokenId} was already gone", tokenId);
                return;
            }

            await ReadResultAsync(response, "delete Access service token");
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Access service token {TokenId}", tokenId);
            throw new CloudflareAccessServiceException("Could not delete the Access service token", ex);
        }
    }

    // ───────────────────────────────────────────────────────────
    // IDENTITY PROVIDERS
    // ───────────────────────────────────────────────────────────

    public async Task<List<AccessIdentityProvider>> ListIdentityProvidersAsync(
        string apiToken,
        string accountId)
    {
        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.GetAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/access/identity_providers");

            var root = await ReadResultAsync(response, "list Access identity providers");
            var providers = new List<AccessIdentityProvider>();

            if (root.ValueKind != JsonValueKind.Array)
            {
                return providers;
            }

            foreach (var provider in root.EnumerateArray())
            {
                providers.Add(new AccessIdentityProvider
                {
                    Id = ReadString(provider, "id") ?? string.Empty,
                    Name = ReadString(provider, "name") ?? string.Empty,
                    Type = ReadString(provider, "type") ?? string.Empty
                });
            }

            return providers;
        }
        catch (CloudflareAccessServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Access identity providers");
            throw new CloudflareAccessServiceException("Could not list Access identity providers", ex);
        }
    }

    // ───────────────────────────────────────────────────────────
    // PAYLOAD BUILDERS
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Omits optional fields rather than sending nulls, so Cloudflare keeps its own
    /// defaults (for example every account IdP when none is named).
    /// </summary>
    private static Dictionary<string, object> BuildApplicationPayload(AccessApplicationRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["name"] = request.Name,
            ["domain"] = request.Domain,
            ["type"] = request.Type
        };

        if (!string.IsNullOrWhiteSpace(request.SessionDuration))
        {
            payload["session_duration"] = request.SessionDuration.Trim();
        }

        if (request.AllowedIdpIds.Count > 0)
        {
            payload["allowed_idps"] = request.AllowedIdpIds;
        }

        // Reusable policies are attached as links. Precedence is 1-based and ascending,
        // and Cloudflare rejects the app if two links share a precedence.
        payload["policies"] = request.PolicyIds
            .Select((policyId, index) => new Dictionary<string, object>
            {
                ["id"] = policyId,
                ["precedence"] = index + 1
            })
            .ToList();

        return payload;
    }

    private static Dictionary<string, object> BuildPolicyPayload(AccessPolicyRequest request)
    {
        return new Dictionary<string, object>
        {
            ["name"] = request.Name,
            ["decision"] = request.Decision,
            ["include"] = request.Include
        };
    }

    // ───────────────────────────────────────────────────────────
    // PARSERS
    // ───────────────────────────────────────────────────────────

    private static AccessApplication ParseApplication(JsonElement app)
    {
        var parsed = new AccessApplication
        {
            Id = ReadString(app, "id") ?? string.Empty,
            Name = ReadString(app, "name") ?? string.Empty,
            Domain = ReadString(app, "domain") ?? string.Empty,
            Type = ReadString(app, "type") ?? string.Empty,
            SessionDuration = ReadString(app, "session_duration"),
            Aud = ReadString(app, "aud")
        };

        if (app.TryGetProperty("allowed_idps", out var idps) && idps.ValueKind == JsonValueKind.Array)
        {
            foreach (var idp in idps.EnumerateArray())
            {
                var id = idp.ValueKind == JsonValueKind.String ? idp.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    parsed.AllowedIdpIds.Add(id);
                }
            }
        }

        if (app.TryGetProperty("policies", out var policies) && policies.ValueKind == JsonValueKind.Array)
        {
            foreach (var policy in policies.EnumerateArray())
            {
                if (policy.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                parsed.Policies.Add(new AccessApplicationPolicyLink
                {
                    Id = ReadString(policy, "id") ?? string.Empty,
                    Name = ReadString(policy, "name"),
                    Decision = ReadString(policy, "decision"),
                    Precedence = policy.TryGetProperty("precedence", out var precedence)
                                 && precedence.ValueKind == JsonValueKind.Number
                        ? precedence.GetInt32()
                        : null
                });
            }
        }

        return parsed;
    }

    private static AccessPolicy ParsePolicy(JsonElement policy)
    {
        var parsed = new AccessPolicy
        {
            Id = ReadString(policy, "id") ?? string.Empty,
            Name = ReadString(policy, "name") ?? string.Empty,
            Decision = ReadString(policy, "decision") ?? string.Empty
        };

        if (policy.TryGetProperty("include", out var include) && include.ValueKind == JsonValueKind.Array)
        {
            foreach (var rule in include.EnumerateArray())
            {
                var summary = SummariseRule(rule);
                if (summary is not null)
                {
                    parsed.IncludeSummary.Add(summary);
                }
            }
        }

        return parsed;
    }

    /// <summary>
    /// Renders one include rule as a short line for the status page. Rule types StageZero
    /// does not write are shown by their type name rather than dropped, so a policy edited
    /// in the Cloudflare dashboard still reads sensibly.
    /// </summary>
    private static string? SummariseRule(JsonElement rule)
    {
        if (rule.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in rule.EnumerateObject())
        {
            var value = property.Value;

            var detail = property.Name switch
            {
                "email" => ReadString(value, "email"),
                "email_domain" => ReadString(value, "domain"),
                "service_token" => ReadString(value, "token_id"),
                _ => null
            };

            return property.Name switch
            {
                "email" => $"email: {detail}",
                "email_domain" => $"email domain: {detail}",
                "service_token" => $"service token: {detail}",
                "any_valid_service_token" => "any valid service token",
                "everyone" => "everyone",
                _ => property.Name.Replace('_', ' ')
            };
        }

        return null;
    }

    private static AccessServiceTokenInfo ParseServiceToken(JsonElement token)
    {
        return new AccessServiceTokenInfo
        {
            Id = ReadString(token, "id") ?? string.Empty,
            Name = ReadString(token, "name") ?? string.Empty,
            ClientId = ReadString(token, "client_id"),
            Duration = ReadString(token, "duration"),
            ExpiresAt = ReadDateTime(token, "expires_at")
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static DateTime? ReadDateTime(JsonElement element, string propertyName)
    {
        var raw = ReadString(element, propertyName);
        return DateTime.TryParse(raw, out var parsed) ? parsed.ToUniversalTime() : null;
    }

    // ───────────────────────────────────────────────────────────
    // HELPERS
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Validates a Cloudflare response and returns a clone of its "result" element.
    /// The clone outlives the JsonDocument, so callers can read it after disposal.
    /// </summary>
    private async Task<JsonElement> ReadResultAsync(HttpResponseMessage response, string operation)
    {
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var detail = ExtractErrorMessage(responseBody);

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                _logger.LogError("Cloudflare denied the token permission to {Operation}: {StatusCode}",
                    operation, response.StatusCode);
                throw new CloudflareAccessServiceException(
                    $"The Cloudflare API token is not allowed to {operation}. Access objects are "
                    + "account-scoped: the token needs Access: Apps Edit and, for service tokens, "
                    + $"Access: Service Tokens Edit. ({response.StatusCode} — {detail})");
            }

            _logger.LogError("Cloudflare API failed to {Operation}: {StatusCode} {Detail}",
                operation, response.StatusCode, detail);
            throw new CloudflareAccessServiceException(
                $"Cloudflare API error while trying to {operation}: {response.StatusCode} — {detail}");
        }

        using var doc = JsonDocument.Parse(responseBody);

        // Cloudflare returns 200 with success:false for some validation failures.
        if (doc.RootElement.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            var detail = ExtractErrorMessage(responseBody);
            _logger.LogError("Cloudflare API rejected {Operation}: {Detail}", operation, detail);
            throw new CloudflareAccessServiceException(
                $"Cloudflare rejected the request to {operation}: {detail}");
        }

        return doc.RootElement.TryGetProperty("result", out var result)
            ? result.Clone()
            : default;
    }

    /// <summary>
    /// Pulls the messages out of a Cloudflare error envelope. Unlike the other services,
    /// this never falls back to echoing the raw body: an Access response body can carry a
    /// freshly minted client secret, and error text is surfaced to the user and the log.
    /// </summary>
    private static string ExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList();

                if (messages.Count > 0)
                {
                    return string.Join("; ", messages);
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to the generic message below.
        }

        return "no detail returned";
    }

    private static StringContent JsonBody(object payload)
    {
        return new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
    }

    private HttpClient CreateAuthenticatedClient(string apiToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        return httpClient;
    }
}
