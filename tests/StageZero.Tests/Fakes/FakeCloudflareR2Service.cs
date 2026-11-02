using StageZero.Services.Storage;

namespace StageZero.Tests.Fakes;

/// <summary>A Cloudflare account's R2 buckets and their domains, in memory.</summary>
public class FakeCloudflareR2Service : ICloudflareR2Service
{
    public List<R2Bucket> Buckets { get; } = new();
    public Dictionary<string, R2ManagedDomain> Managed { get; } = new();
    public Dictionary<string, List<R2CustomDomain>> Custom { get; } = new();
    public List<string> Deleted { get; } = new();

    /// <summary>Bucket names Cloudflare refuses to delete (they still hold objects).</summary>
    public HashSet<string> NotEmpty { get; } = new();

    public Task<List<R2Bucket>> ListBucketsAsync(string apiToken, string accountId) => Task.FromResult(Buckets.ToList());

    public Task<R2Bucket> CreateBucketAsync(string apiToken, string accountId, string name, string? locationHint, string? storageClass)
    {
        var bucket = new R2Bucket { Name = name, Location = locationHint, StorageClass = storageClass ?? "Standard", CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) };
        Buckets.Add(bucket);
        return Task.FromResult(bucket);
    }

    public Task DeleteBucketAsync(string apiToken, string accountId, string name)
    {
        if (NotEmpty.Contains(name))
        {
            throw new CloudflareR2ServiceException($"Cloudflare could not delete the bucket {name}: Conflict — The bucket you tried to delete is not empty");
        }

        Buckets.RemoveAll(b => b.Name == name);
        Deleted.Add(name);
        return Task.CompletedTask;
    }

    public Task<R2ManagedDomain> GetManagedDomainAsync(string apiToken, string accountId, string bucket) =>
        Task.FromResult(Managed.TryGetValue(bucket, out var m) ? m : new R2ManagedDomain { Domain = $"pub-{bucket}.r2.dev", Enabled = false });

    public Task<R2ManagedDomain> SetManagedDomainAsync(string apiToken, string accountId, string bucket, bool enabled)
    {
        var m = new R2ManagedDomain { Domain = $"pub-{bucket}.r2.dev", Enabled = enabled };
        Managed[bucket] = m;
        return Task.FromResult(m);
    }

    public Task<List<R2CustomDomain>> ListCustomDomainsAsync(string apiToken, string accountId, string bucket) =>
        Task.FromResult(Custom.TryGetValue(bucket, out var list) ? list.ToList() : new List<R2CustomDomain>());

    public Task<R2CustomDomain> AddCustomDomainAsync(string apiToken, string accountId, string bucket, string domain, string zoneId, bool enabled, string? minTls)
    {
        var d = new R2CustomDomain { Domain = domain, ZoneId = zoneId, Enabled = enabled, MinTls = minTls ?? "1.0", OwnershipStatus = "pending", SslStatus = "initializing" };
        if (!Custom.TryGetValue(bucket, out var list)) Custom[bucket] = list = new List<R2CustomDomain>();
        list.Add(d);
        return Task.FromResult(d);
    }

    public Task<R2CustomDomain> UpdateCustomDomainAsync(string apiToken, string accountId, string bucket, string domain, bool enabled, string? minTls)
    {
        var d = Custom.TryGetValue(bucket, out var list) ? list.FirstOrDefault(x => x.Domain == domain) : null;
        if (d is null) throw new CloudflareR2ServiceException($"Cloudflare could not update {domain} on {bucket}: NotFound — custom domain not found");
        d.Enabled = enabled;
        if (minTls is not null) d.MinTls = minTls;
        return Task.FromResult(d);
    }

    public Task RemoveCustomDomainAsync(string apiToken, string accountId, string bucket, string domain)
    {
        if (Custom.TryGetValue(bucket, out var list)) list.RemoveAll(x => x.Domain == domain);
        return Task.CompletedTask;
    }
}
