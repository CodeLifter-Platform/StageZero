using StageZero.Services.Access;

namespace StageZero.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the Cloudflare Access API, mocked at the same layer the rest of
/// the app talks to it — the ICloudflareAccessService interface, one level below the
/// provisioning service under test.
///
/// It behaves like the real API in the ways the tests care about: objects it has not been
/// given do not exist, creates return new IDs, and every call is recorded so a test can
/// assert that a re-run updated rather than created.
/// </summary>
public class FakeCloudflareAccessService : ICloudflareAccessService
{
    private int _nextId = 1;

    public List<AccessApplication> Applications { get; } = new();
    public List<AccessPolicy> Policies { get; } = new();
    public List<AccessServiceTokenInfo> ServiceTokens { get; } = new();
    public List<AccessIdentityProvider> IdentityProviders { get; } = new();

    /// <summary>Method names in call order, for asserting create-versus-update behaviour.</summary>
    public List<string> Calls { get; } = new();

    /// <summary>Requests as they were sent, so policy construction can be inspected.</summary>
    public List<AccessPolicyRequest> PolicyRequests { get; } = new();
    public List<AccessApplicationRequest> ApplicationRequests { get; } = new();

    /// <summary>Set to make the permission probe report a token that cannot manage Access.</summary>
    public bool CanManageApplications { get; set; } = true;
    public bool CanManageServiceTokens { get; set; } = true;

    /// <summary>When set, the named method throws instead of running.</summary>
    public string? FailOnCall { get; set; }

    public Task<AccessPermissionCheck> CheckPermissionsAsync(string apiToken, string accountId)
    {
        Record(nameof(CheckPermissionsAsync));

        var check = new AccessPermissionCheck
        {
            CanManageApplications = CanManageApplications,
            CanManageServiceTokens = CanManageServiceTokens
        };

        if (!CanManageApplications)
        {
            check.MissingPermissions.Add("Access: Apps Read and Access: Apps Edit");
        }

        if (!CanManageServiceTokens)
        {
            check.MissingPermissions.Add("Access: Service Tokens Read and Access: Service Tokens Edit");
        }

        return Task.FromResult(check);
    }

    // ── Applications ────────────────────────────────────────────

    public Task<AccessApplication?> FindApplicationByDomainAsync(
        string apiToken, string accountId, string hostname)
    {
        Record(nameof(FindApplicationByDomainAsync));
        return Task.FromResult(Applications.FirstOrDefault(a =>
            string.Equals(a.Domain, hostname, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<AccessApplication?> GetApplicationAsync(
        string apiToken, string accountId, string applicationId)
    {
        Record(nameof(GetApplicationAsync));
        return Task.FromResult(Applications.FirstOrDefault(a => a.Id == applicationId));
    }

    public Task<AccessApplication> CreateApplicationAsync(
        string apiToken, string accountId, AccessApplicationRequest request)
    {
        Record(nameof(CreateApplicationAsync));
        ApplicationRequests.Add(request);

        var application = ToApplication(NextId("app"), request);
        Applications.Add(application);

        return Task.FromResult(application);
    }

    public Task<AccessApplication> UpdateApplicationAsync(
        string apiToken, string accountId, string applicationId, AccessApplicationRequest request)
    {
        Record(nameof(UpdateApplicationAsync));
        ApplicationRequests.Add(request);

        var existing = Applications.FirstOrDefault(a => a.Id == applicationId)
                       ?? throw new CloudflareAccessServiceException($"No application {applicationId}");

        var updated = ToApplication(applicationId, request);
        Applications[Applications.IndexOf(existing)] = updated;

        return Task.FromResult(updated);
    }

    public Task DeleteApplicationAsync(string apiToken, string accountId, string applicationId)
    {
        Record(nameof(DeleteApplicationAsync));
        Applications.RemoveAll(a => a.Id == applicationId);
        return Task.CompletedTask;
    }

    // ── Policies ────────────────────────────────────────────────

    public Task<List<AccessPolicy>> ListPoliciesAsync(string apiToken, string accountId)
    {
        Record(nameof(ListPoliciesAsync));
        return Task.FromResult(Policies.ToList());
    }

    public Task<AccessPolicy?> GetPolicyAsync(string apiToken, string accountId, string policyId)
    {
        Record(nameof(GetPolicyAsync));
        return Task.FromResult(Policies.FirstOrDefault(p => p.Id == policyId));
    }

    public Task<AccessPolicy> CreatePolicyAsync(
        string apiToken, string accountId, AccessPolicyRequest request)
    {
        Record(nameof(CreatePolicyAsync));
        PolicyRequests.Add(request);

        var policy = ToPolicy(NextId("pol"), request);
        Policies.Add(policy);

        return Task.FromResult(policy);
    }

    public Task<AccessPolicy> UpdatePolicyAsync(
        string apiToken, string accountId, string policyId, AccessPolicyRequest request)
    {
        Record(nameof(UpdatePolicyAsync));
        PolicyRequests.Add(request);

        var existing = Policies.FirstOrDefault(p => p.Id == policyId)
                       ?? throw new CloudflareAccessServiceException($"No policy {policyId}");

        var updated = ToPolicy(policyId, request);
        Policies[Policies.IndexOf(existing)] = updated;

        return Task.FromResult(updated);
    }

    public Task DeletePolicyAsync(string apiToken, string accountId, string policyId)
    {
        Record(nameof(DeletePolicyAsync));
        Policies.RemoveAll(p => p.Id == policyId);
        return Task.CompletedTask;
    }

    // ── Service tokens ──────────────────────────────────────────

    public Task<List<AccessServiceTokenInfo>> ListServiceTokensAsync(string apiToken, string accountId)
    {
        Record(nameof(ListServiceTokensAsync));
        return Task.FromResult(ServiceTokens.ToList());
    }

    public Task<MintedServiceToken> CreateServiceTokenAsync(
        string apiToken, string accountId, string name, string? duration)
    {
        Record(nameof(CreateServiceTokenAsync));

        var id = NextId("tok");
        ServiceTokens.Add(new AccessServiceTokenInfo
        {
            Id = id,
            Name = name,
            ClientId = $"{id}.access",
            Duration = duration
        });

        return Task.FromResult(new MintedServiceToken
        {
            TokenId = id,
            Name = name,
            ClientId = $"{id}.access",
            ClientSecret = $"secret-for-{id}",
            Duration = duration
        });
    }

    public Task DeleteServiceTokenAsync(string apiToken, string accountId, string tokenId)
    {
        Record(nameof(DeleteServiceTokenAsync));
        ServiceTokens.RemoveAll(t => t.Id == tokenId);
        return Task.CompletedTask;
    }

    // ── Identity providers ──────────────────────────────────────

    public Task<List<AccessIdentityProvider>> ListIdentityProvidersAsync(string apiToken, string accountId)
    {
        Record(nameof(ListIdentityProvidersAsync));
        return Task.FromResult(IdentityProviders.ToList());
    }

    // ── Helpers ─────────────────────────────────────────────────

    public int CountCalls(string method) => Calls.Count(c => c == method);

    private void Record(string method)
    {
        Calls.Add(method);

        if (FailOnCall == method)
        {
            throw new CloudflareAccessServiceException($"Fake failure in {method}");
        }
    }

    private string NextId(string prefix) => $"{prefix}-{_nextId++}";

    private static AccessApplication ToApplication(string id, AccessApplicationRequest request) =>
        new()
        {
            Id = id,
            Name = request.Name,
            Domain = request.Domain,
            Type = request.Type,
            SessionDuration = request.SessionDuration,
            Aud = $"aud-{id}",
            AllowedIdpIds = request.AllowedIdpIds.ToList(),
            Policies = request.PolicyIds
                .Select((policyId, index) => new AccessApplicationPolicyLink
                {
                    Id = policyId,
                    Precedence = index + 1
                })
                .ToList()
        };

    private static AccessPolicy ToPolicy(string id, AccessPolicyRequest request) =>
        new()
        {
            Id = id,
            Name = request.Name,
            Decision = request.Decision,
            IncludeSummary = request.Include.Select(rule => string.Join(",", rule.Keys)).ToList()
        };
}
