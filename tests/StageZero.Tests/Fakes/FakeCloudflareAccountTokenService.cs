using StageZero.Services.Storage;

namespace StageZero.Tests.Fakes;

/// <summary>A Cloudflare account's API tokens in memory: Cloudflare's permission groups, and the tokens minted from them.</summary>
public class FakeCloudflareAccountTokenService : ICloudflareAccountTokenService
{
    public const string BucketWriteGroupId = "pg-bucket-write";
    public const string BucketReadGroupId = "pg-bucket-read";

    public List<CloudflarePermissionGroup> PermissionGroups { get; } =
    [
        new() { Id = "pg-r2-write", Name = "Workers R2 Storage Write", Scopes = ["com.cloudflare.api.account"] },
        new() { Id = "pg-r2-read", Name = "Workers R2 Storage Read", Scopes = ["com.cloudflare.api.account"] },
        new() { Id = BucketWriteGroupId, Name = "Workers R2 Storage Bucket Item Write", Scopes = ["com.cloudflare.edge.r2.bucket"] },
        new() { Id = BucketReadGroupId, Name = "Workers R2 Storage Bucket Item Read", Scopes = ["com.cloudflare.edge.r2.bucket"] },
        new() { Id = "pg-dns", Name = "DNS Write", Scopes = ["com.cloudflare.api.account.zone"] },
    ];

    public List<CloudflareAccountToken> Tokens { get; } = new();

    /// <summary>What CreateTokenAsync was asked for, in order.</summary>
    public List<(string Name, IReadOnlyList<CloudflareTokenPolicyRequest> Policies, DateTime? ExpiresOn)> Created { get; } = new();

    public List<string> Deleted { get; } = new();
    public List<string> Rolled { get; } = new();

    /// <summary>When set, Cloudflare refuses the permission-group list (no API Tokens permission).</summary>
    public bool RefuseGroups { get; set; }

    private int _next;

    public Task<List<CloudflarePermissionGroup>> ListPermissionGroupsAsync(string apiToken, string accountId) =>
        RefuseGroups
            ? throw new CloudflareAccountTokenServiceException("Cloudflare could not list the permission groups: Forbidden — Authentication error")
            : Task.FromResult(PermissionGroups.ToList());

    public Task<List<CloudflareAccountToken>> ListTokensAsync(string apiToken, string accountId) =>
        Task.FromResult(Tokens.Select(t => new CloudflareAccountToken
        {
            Id = t.Id, Name = t.Name, Status = t.Status, IssuedOn = t.IssuedOn, ExpiresOn = t.ExpiresOn, LastUsedOn = t.LastUsedOn, Policies = t.Policies, Value = null
        }).ToList());

    public Task<CloudflareAccountToken> CreateTokenAsync(string apiToken, string accountId, string name, IReadOnlyList<CloudflareTokenPolicyRequest> policies, DateTime? expiresOn)
    {
        Created.Add((name, policies, expiresOn));
        var n = ++_next;
        var token = new CloudflareAccountToken
        {
            Id = $"token-{n}",
            Name = name,
            Status = "active",
            IssuedOn = new DateTime(2026, 11, 2, 14, 0, 0, DateTimeKind.Utc),
            ExpiresOn = expiresOn,
            Value = $"cf-token-value-{n}",
            Policies = policies.Select(p => new CloudflareTokenPolicy
            {
                PermissionGroups = p.Groups.Select(g => g.Name).ToList(), Resources = p.Resources.Keys.ToList()
            }).ToList(),
        };
        Tokens.Add(token);
        return Task.FromResult(token);
    }

    public Task DeleteTokenAsync(string apiToken, string accountId, string tokenId)
    {
        Deleted.Add(tokenId);
        Tokens.RemoveAll(t => t.Id == tokenId);
        return Task.CompletedTask;
    }

    public Task<string> RollTokenAsync(string apiToken, string accountId, string tokenId)
    {
        if (Tokens.All(t => t.Id != tokenId)) throw new CloudflareAccountTokenServiceException($"Cloudflare could not roll the API token {tokenId}: NotFound — token not found");
        Rolled.Add(tokenId);
        return Task.FromResult($"rolled-value-{tokenId}");
    }
}
