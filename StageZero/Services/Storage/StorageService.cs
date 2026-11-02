using System.Text.RegularExpressions;
using StageZero.Services.Cloudflare;
using StageZero.Services.Dns;

namespace StageZero.Services.Storage;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// R2 buckets and the hostnames in front of them, with the rules the API enforces: bucket names
/// Cloudflare will take, a custom domain only in a zone the saved token can see, and nothing
/// taken from the caller that Cloudflare can tell us itself. Every call uses the token and
/// account saved on the Settings page.
/// </summary>
public interface IStorageService
{
    Task<List<R2Bucket>> GetBucketsAsync();
    Task<R2Bucket> CreateBucketAsync(string name, string? locationHint, string? storageClass);
    Task DeleteBucketAsync(string name);

    /// <summary>The r2.dev domain, the custom domains, and the zones a new one may be in.</summary>
    Task<BucketDomains> GetDomainsAsync(string bucket);

    Task<R2ManagedDomain> SetPublicAccessAsync(string bucket, bool enabled);
    Task<R2CustomDomain> AddCustomDomainAsync(string bucket, string domain, bool enabled, string? minTls);
    Task<R2CustomDomain> UpdateCustomDomainAsync(string bucket, string domain, bool enabled, string? minTls);
    Task RemoveCustomDomainAsync(string bucket, string domain);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record BucketDomains(R2ManagedDomain Managed, IReadOnlyList<R2CustomDomain> Custom, IReadOnlyList<CloudflareZone> Zones);

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public partial class StorageService : IStorageService
{
    /// <summary>Cloudflare's location hints.</summary>
    public static readonly IReadOnlyList<string> LocationHints = ["wnam", "enam", "weur", "eeur", "apac", "oc"];

    public static readonly IReadOnlyList<string> StorageClasses = ["Standard", "InfrequentAccess"];

    public static readonly IReadOnlyList<string> MinTlsVersions = ["1.0", "1.1", "1.2", "1.3"];

    private readonly ILogger<StorageService> _logger;
    private readonly ICloudflareCredentialService _credentials;
    private readonly ICloudflareR2Service _r2;
    private readonly ICloudflareService _dns;

    public StorageService(
        ILogger<StorageService> logger,
        ICloudflareCredentialService credentials,
        ICloudflareR2Service r2,
        ICloudflareService dns)
    {
        _logger = logger;
        _credentials = credentials;
        _r2 = r2;
        _dns = dns;
    }

    // ── rules ────────────────────────────────────────────────────

    /// <summary>
    /// Why a bucket name is unusable, or null. Cloudflare's rule: 3 to 63 characters, lowercase
    /// letters, digits and hyphens, starting and ending with a letter or digit.
    /// </summary>
    public static string? BucketNameProblem(string? name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) return "Bucket name is required.";
        if (name.Length is < 3 or > 63) return "A bucket name is 3 to 63 characters.";
        if (!BucketName().IsMatch(name)) return "A bucket name uses lowercase letters, digits and hyphens, and starts and ends with a letter or digit.";
        return null;
    }

    /// <summary>Why a hostname is unusable as a custom domain, or null.</summary>
    public static string? DomainProblem(string? domain)
    {
        domain = domain?.Trim().TrimEnd('.').ToLowerInvariant() ?? string.Empty;
        if (domain.Length == 0) return "Hostname is required.";
        if (domain.Length > 253 || !Hostname().IsMatch(domain) || !domain.Contains('.')) return "Enter a full hostname, like files.example.com.";
        return null;
    }

    /// <summary>The zone a hostname belongs to: the longest zone name it ends with.</summary>
    public static CloudflareZone? ZoneFor(string domain, IEnumerable<CloudflareZone> zones)
    {
        domain = domain.Trim().TrimEnd('.').ToLowerInvariant();
        return zones
            .Where(z => domain == z.Name.ToLowerInvariant() || domain.EndsWith("." + z.Name.ToLowerInvariant(), StringComparison.Ordinal))
            .OrderByDescending(z => z.Name.Length)
            .FirstOrDefault();
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$")]
    private static partial Regex BucketName();

    [GeneratedRegex(@"^(?!-)[a-z0-9-]{1,63}(?<!-)(\.(?!-)[a-z0-9-]{1,63}(?<!-))+$")]
    private static partial Regex Hostname();

    // ── buckets ──────────────────────────────────────────────────

    public async Task<List<R2Bucket>> GetBucketsAsync()
    {
        var (apiToken, accountId) = await RequireCredentialsAsync();
        return await Cloudflare(() => _r2.ListBucketsAsync(apiToken, accountId), "list the buckets");
    }

    public async Task<R2Bucket> CreateBucketAsync(string name, string? locationHint, string? storageClass)
    {
        var (apiToken, accountId) = await RequireCredentialsAsync();
        name = name?.Trim() ?? string.Empty;
        if (BucketNameProblem(name) is { } problem) throw new UserFacingException(problem);
        locationHint = Choice(locationHint, LocationHints, "location hint");
        storageClass = Choice(storageClass, StorageClasses, "storage class");

        var existing = await Cloudflare(() => _r2.ListBucketsAsync(apiToken, accountId), "list the buckets");
        if (existing.Any(b => string.Equals(b.Name, name, StringComparison.Ordinal)))
        {
            throw UserFacingException.Conflict($"A bucket named {name} already exists on this account.");
        }

        var created = await Cloudflare(() => _r2.CreateBucketAsync(apiToken, accountId, name, locationHint, storageClass), $"create the bucket {name}");
        _logger.LogInformation("Created R2 bucket {Bucket}", created.Name);
        return created;
    }

    public async Task DeleteBucketAsync(string name)
    {
        var (apiToken, accountId) = await RequireCredentialsAsync();
        name = name?.Trim() ?? string.Empty;
        if (BucketNameProblem(name) is { } problem) throw new UserFacingException(problem);

        // Cloudflare refuses to delete a bucket that still holds objects; its message says so.
        await Cloudflare(() => _r2.DeleteBucketAsync(apiToken, accountId, name), $"delete the bucket {name}");
    }

    // ── domains ──────────────────────────────────────────────────

    public async Task<BucketDomains> GetDomainsAsync(string bucket)
    {
        var (apiToken, accountId) = await RequireCredentialsAsync();
        bucket = RequireBucketName(bucket);

        var managed = await Cloudflare(() => _r2.GetManagedDomainAsync(apiToken, accountId, bucket), $"read the public access of {bucket}");
        var custom = await Cloudflare(() => _r2.ListCustomDomainsAsync(apiToken, accountId, bucket), $"list the custom domains of {bucket}");
        var zones = await Cloudflare(() => _dns.GetZonesAsync(apiToken), "list the zones");
        return new BucketDomains(managed, custom, zones);
    }

    public async Task<R2ManagedDomain> SetPublicAccessAsync(string bucket, bool enabled)
    {
        var (apiToken, accountId) = await RequireCredentialsAsync();
        bucket = RequireBucketName(bucket);
        return await Cloudflare(() => _r2.SetManagedDomainAsync(apiToken, accountId, bucket, enabled),
            $"{(enabled ? "enable" : "disable")} public access on {bucket}");
    }

    public async Task<R2CustomDomain> AddCustomDomainAsync(string bucket, string domain, bool enabled, string? minTls)
    {
        var (apiToken, accountId) = await RequireCredentialsAsync();
        bucket = RequireBucketName(bucket);
        domain = RequireDomain(domain);
        minTls = Choice(minTls, MinTlsVersions, "minimum TLS version");

        // The zone comes from Cloudflare, never from the caller: the hostname must sit in one the token can see.
        var zones = await Cloudflare(() => _dns.GetZonesAsync(apiToken), "list the zones");
        var zone = ZoneFor(domain, zones)
                   ?? throw new UserFacingException(
                       $"{domain} isn't in a zone this token can see" + (zones.Count == 0 ? "." : $" ({string.Join(", ", zones.Select(z => z.Name))}).")
                       + " Use a hostname in one of them, or include the zone in the token's Zone Resources.");

        var attached = await Cloudflare(() => _r2.AddCustomDomainAsync(apiToken, accountId, bucket, domain, zone.Id, enabled, minTls), $"attach {domain} to {bucket}");
        attached.ZoneName ??= zone.Name;
        _logger.LogInformation("Attached {Domain} (zone {Zone}) to R2 bucket {Bucket}", domain, zone.Name, bucket);
        return attached;
    }

    public async Task<R2CustomDomain> UpdateCustomDomainAsync(string bucket, string domain, bool enabled, string? minTls)
    {
        var (apiToken, accountId) = await RequireCredentialsAsync();
        bucket = RequireBucketName(bucket);
        domain = RequireDomain(domain);
        minTls = Choice(minTls, MinTlsVersions, "minimum TLS version");
        return await Cloudflare(() => _r2.UpdateCustomDomainAsync(apiToken, accountId, bucket, domain, enabled, minTls), $"update {domain} on {bucket}");
    }

    public async Task RemoveCustomDomainAsync(string bucket, string domain)
    {
        var (apiToken, accountId) = await RequireCredentialsAsync();
        bucket = RequireBucketName(bucket);
        domain = RequireDomain(domain);
        await Cloudflare(() => _r2.RemoveCustomDomainAsync(apiToken, accountId, bucket, domain), $"remove {domain} from {bucket}");
    }

    // ── helpers ──────────────────────────────────────────────────

    private async Task<(string ApiToken, string AccountId)> RequireCredentialsAsync()
    {
        var credentials = await _credentials.RequireAsync(needsAccount: true);
        return (credentials.ApiToken, credentials.AccountId!);
    }

    private static string RequireBucketName(string? bucket)
    {
        bucket = bucket?.Trim() ?? string.Empty;
        return BucketNameProblem(bucket) is { } problem ? throw new UserFacingException(problem) : bucket;
    }

    private static string RequireDomain(string? domain)
    {
        if (DomainProblem(domain) is { } problem) throw new UserFacingException(problem);
        return domain!.Trim().TrimEnd('.').ToLowerInvariant();
    }

    /// <summary>A value from a fixed list, or null for "let Cloudflare choose".</summary>
    private static string? Choice(string? value, IReadOnlyList<string> allowed, string what)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = allowed.FirstOrDefault(a => string.Equals(a, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? throw new UserFacingException($"Unknown {what} {value}. Choose one of {string.Join(", ", allowed)}.");
    }

    /// <summary>A Cloudflare call whose failure the operator should read, as a 502 with the detail.</summary>
    private async Task<T> Cloudflare<T>(Func<Task<T>> call, string operation)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogError(ex, "Cloudflare R2: could not {Operation}", operation);
            throw new UserFacingException($"Could not {operation}: {ex.Message}", ex);
        }
    }

    private async Task Cloudflare(Func<Task> call, string operation)
    {
        await Cloudflare(async () => { await call(); return true; }, operation);
    }
}
