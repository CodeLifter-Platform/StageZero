using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StageZero.Services.Storage;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Cloudflare's R2 REST API for buckets and the domains in front of them. Account-scoped, like
/// the tunnel: every call takes the saved token and account. Objects are not here; they go
/// through R2's S3 endpoint, which is a different credential and a later feature.
/// </summary>
public interface ICloudflareR2Service
{
    Task<List<R2Bucket>> ListBucketsAsync(string apiToken, string accountId);
    Task<R2Bucket> CreateBucketAsync(string apiToken, string accountId, string name, string? locationHint, string? storageClass);
    Task DeleteBucketAsync(string apiToken, string accountId, string name);

    /// <summary>The bucket's r2.dev address and whether it is switched on.</summary>
    Task<R2ManagedDomain> GetManagedDomainAsync(string apiToken, string accountId, string bucket);
    Task<R2ManagedDomain> SetManagedDomainAsync(string apiToken, string accountId, string bucket, bool enabled);

    Task<List<R2CustomDomain>> ListCustomDomainsAsync(string apiToken, string accountId, string bucket);

    /// <summary>Attaches a hostname in one of the account's zones. Cloudflare writes the DNS record itself.</summary>
    Task<R2CustomDomain> AddCustomDomainAsync(string apiToken, string accountId, string bucket, string domain, string zoneId, bool enabled, string? minTls);
    Task<R2CustomDomain> UpdateCustomDomainAsync(string apiToken, string accountId, string bucket, string domain, bool enabled, string? minTls);
    Task RemoveCustomDomainAsync(string apiToken, string accountId, string bucket, string domain);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

public sealed class R2Bucket
{
    public string Name { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }

    /// <summary>Cloudflare's location hint: wnam, enam, weur, eeur, apac or oc. Null when it chose.</summary>
    public string? Location { get; set; }

    /// <summary>"Standard" or "InfrequentAccess".</summary>
    public string? StorageClass { get; set; }

    public string? Jurisdiction { get; set; }
}

public sealed class R2ManagedDomain
{
    /// <summary>The pub-….r2.dev hostname Cloudflare assigned.</summary>
    public string Domain { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public sealed class R2CustomDomain
{
    public string Domain { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? ZoneId { get; set; }
    public string? ZoneName { get; set; }
    public string? MinTls { get; set; }

    /// <summary>pending, active, deactivated, blocked, error or unknown.</summary>
    public string? OwnershipStatus { get; set; }

    /// <summary>initializing, pending, active, deactivated, error or unknown.</summary>
    public string? SslStatus { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class CloudflareR2ServiceException : Exception
{
    public CloudflareR2ServiceException(string message) : base(message) { }
    public CloudflareR2ServiceException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class CloudflareR2Service : ICloudflareR2Service
{
    private const string CLOUDFLARE_API_BASE = "https://api.cloudflare.com/client/v4";

    private readonly ILogger<CloudflareR2Service> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public CloudflareR2Service(ILogger<CloudflareR2Service> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    // ───────────────────────────────────────────────────────────
    // BUCKETS
    // ───────────────────────────────────────────────────────────

    public async Task<List<R2Bucket>> ListBucketsAsync(string apiToken, string accountId)
    {
        var httpClient = CreateAuthenticatedClient(apiToken);
        var buckets = new List<R2Bucket>();
        string? cursor = null;
        do
        {
            var url = $"{Buckets(accountId)}?per_page=1000" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var (result, info) = await ReadAsync(await httpClient.GetAsync(url), "list buckets");
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("buckets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                buckets.AddRange(list.EnumerateArray().Select(ParseBucket));
            }

            cursor = info.ValueKind == JsonValueKind.Object && info.TryGetProperty("cursor", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
        while (!string.IsNullOrEmpty(cursor));

        return buckets;
    }

    public async Task<R2Bucket> CreateBucketAsync(string apiToken, string accountId, string name, string? locationHint, string? storageClass)
    {
        _logger.LogInformation("Creating R2 bucket {Bucket}", name);
        var httpClient = CreateAuthenticatedClient(apiToken);
        var payload = new Dictionary<string, object> { ["name"] = name };
        if (!string.IsNullOrEmpty(locationHint)) payload["locationHint"] = locationHint;
        if (!string.IsNullOrEmpty(storageClass)) payload["storageClass"] = storageClass;

        var (result, _) = await ReadAsync(await httpClient.PostAsync(Buckets(accountId), JsonBody(payload)), $"create bucket {name}");
        return result.ValueKind == JsonValueKind.Object ? ParseBucket(result) : new R2Bucket { Name = name, Location = locationHint, StorageClass = storageClass };
    }

    public async Task DeleteBucketAsync(string apiToken, string accountId, string name)
    {
        _logger.LogWarning("Deleting R2 bucket {Bucket}", name);
        var httpClient = CreateAuthenticatedClient(apiToken);
        await ReadAsync(await httpClient.DeleteAsync($"{Buckets(accountId)}/{Uri.EscapeDataString(name)}"), $"delete bucket {name}");
    }

    // ───────────────────────────────────────────────────────────
    // DOMAINS
    // ───────────────────────────────────────────────────────────

    public async Task<R2ManagedDomain> GetManagedDomainAsync(string apiToken, string accountId, string bucket)
    {
        var httpClient = CreateAuthenticatedClient(apiToken);
        var (result, _) = await ReadAsync(await httpClient.GetAsync($"{Domains(accountId, bucket)}/managed"), $"read the r2.dev domain of {bucket}");
        return ParseManaged(result);
    }

    public async Task<R2ManagedDomain> SetManagedDomainAsync(string apiToken, string accountId, string bucket, bool enabled)
    {
        _logger.LogInformation("{Action} public r2.dev access on bucket {Bucket}", enabled ? "Enabling" : "Disabling", bucket);
        var httpClient = CreateAuthenticatedClient(apiToken);
        var (result, _) = await ReadAsync(
            await httpClient.PutAsync($"{Domains(accountId, bucket)}/managed", JsonBody(new { enabled })),
            $"{(enabled ? "enable" : "disable")} the r2.dev domain of {bucket}");
        return ParseManaged(result);
    }

    public async Task<List<R2CustomDomain>> ListCustomDomainsAsync(string apiToken, string accountId, string bucket)
    {
        var httpClient = CreateAuthenticatedClient(apiToken);
        var (result, _) = await ReadAsync(await httpClient.GetAsync($"{Domains(accountId, bucket)}/custom"), $"list the custom domains of {bucket}");
        return result.ValueKind == JsonValueKind.Object && result.TryGetProperty("domains", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(ParseCustom).ToList()
            : [];
    }

    public async Task<R2CustomDomain> AddCustomDomainAsync(string apiToken, string accountId, string bucket, string domain, string zoneId, bool enabled, string? minTls)
    {
        _logger.LogInformation("Attaching custom domain {Domain} to bucket {Bucket}", domain, bucket);
        var httpClient = CreateAuthenticatedClient(apiToken);
        var payload = new Dictionary<string, object> { ["domain"] = domain, ["zoneId"] = zoneId, ["enabled"] = enabled };
        if (!string.IsNullOrEmpty(minTls)) payload["minTLS"] = minTls;

        var (result, _) = await ReadAsync(await httpClient.PostAsync($"{Domains(accountId, bucket)}/custom", JsonBody(payload)), $"attach {domain} to {bucket}");
        var created = result.ValueKind == JsonValueKind.Object ? ParseCustom(result) : new R2CustomDomain { Domain = domain, Enabled = enabled, MinTls = minTls };
        created.ZoneId ??= zoneId;
        return created;
    }

    public async Task<R2CustomDomain> UpdateCustomDomainAsync(string apiToken, string accountId, string bucket, string domain, bool enabled, string? minTls)
    {
        var httpClient = CreateAuthenticatedClient(apiToken);
        var payload = new Dictionary<string, object> { ["enabled"] = enabled };
        if (!string.IsNullOrEmpty(minTls)) payload["minTLS"] = minTls;

        var (result, _) = await ReadAsync(
            await httpClient.PutAsync($"{Domains(accountId, bucket)}/custom/{Uri.EscapeDataString(domain)}", JsonBody(payload)),
            $"update {domain} on {bucket}");
        return result.ValueKind == JsonValueKind.Object ? ParseCustom(result) : new R2CustomDomain { Domain = domain, Enabled = enabled, MinTls = minTls };
    }

    public async Task RemoveCustomDomainAsync(string apiToken, string accountId, string bucket, string domain)
    {
        _logger.LogWarning("Removing custom domain {Domain} from bucket {Bucket}", domain, bucket);
        var httpClient = CreateAuthenticatedClient(apiToken);
        await ReadAsync(await httpClient.DeleteAsync($"{Domains(accountId, bucket)}/custom/{Uri.EscapeDataString(domain)}"), $"remove {domain} from {bucket}");
    }

    // ───────────────────────────────────────────────────────────
    // HELPERS
    // ───────────────────────────────────────────────────────────

    private static string Buckets(string accountId) => $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/r2/buckets";

    private static string Domains(string accountId, string bucket) => $"{Buckets(accountId)}/{Uri.EscapeDataString(bucket)}/domains";

    private static R2Bucket ParseBucket(JsonElement e) => new()
    {
        Name = Str(e, "name") ?? string.Empty,
        CreatedAt = DateTimeOffset.TryParse(Str(e, "creation_date"), out var created) ? created.UtcDateTime : null,
        Location = Str(e, "location"),
        StorageClass = Str(e, "storage_class"),
        Jurisdiction = Str(e, "jurisdiction"),
    };

    private static R2ManagedDomain ParseManaged(JsonElement e) => new()
    {
        Domain = e.ValueKind == JsonValueKind.Object ? Str(e, "domain") ?? string.Empty : string.Empty,
        Enabled = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("enabled", out var on) && on.ValueKind == JsonValueKind.True,
    };

    private static R2CustomDomain ParseCustom(JsonElement e)
    {
        var status = e.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
        return new R2CustomDomain
        {
            Domain = Str(e, "domain") ?? string.Empty,
            Enabled = e.TryGetProperty("enabled", out var on) && on.ValueKind == JsonValueKind.True,
            ZoneId = Str(e, "zoneId"),
            ZoneName = Str(e, "zoneName"),
            MinTls = Str(e, "minTLS"),
            OwnershipStatus = status.ValueKind == JsonValueKind.Object ? Str(status, "ownership") : null,
            SslStatus = status.ValueKind == JsonValueKind.Object ? Str(status, "ssl") : null,
        };
    }

    private static string? Str(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Validates a Cloudflare response; returns clones of its "result" and "result_info".</summary>
    private async Task<(JsonElement Result, JsonElement Info)> ReadAsync(HttpResponseMessage response, string operation)
    {
        var body = await response.Content.ReadAsStringAsync();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch (JsonException ex)
        {
            throw new CloudflareR2ServiceException($"Cloudflare sent an unreadable answer while trying to {operation} ({response.StatusCode}).", ex);
        }

        using (doc)
        {
            var refused = doc.RootElement.ValueKind == JsonValueKind.Object
                          && doc.RootElement.TryGetProperty("success", out var success)
                          && success.ValueKind == JsonValueKind.False;
            if (!response.IsSuccessStatusCode || refused)
            {
                var detail = ExtractErrorMessage(doc.RootElement);
                _logger.LogWarning("Cloudflare R2 API failed to {Operation}: {StatusCode} {Detail}", operation, response.StatusCode, detail);
                throw new CloudflareR2ServiceException($"Cloudflare could not {operation}: {response.StatusCode} — {detail}");
            }

            var root = doc.RootElement;
            var result = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var r) ? r.Clone() : default;
            var info = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result_info", out var i) ? i.Clone() : default;
            return (result, info);
        }
    }

    private static string ExtractErrorMessage(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            var messages = errors.EnumerateArray()
                .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();
            if (messages.Count > 0) return string.Join("; ", messages);
        }

        return "no detail returned";
    }

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private HttpClient CreateAuthenticatedClient(string apiToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        return httpClient;
    }
}
