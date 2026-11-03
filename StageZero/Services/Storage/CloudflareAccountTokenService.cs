using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StageZero.Services.Storage;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Cloudflare's account-owned API tokens (/accounts/{id}/tokens): what StageZero mints a
/// per-app R2 credential from. A token's value comes back from Cloudflare exactly once, on
/// create or roll; this service passes it up and never logs it.
/// </summary>
public interface ICloudflareAccountTokenService
{
    /// <summary>The permission groups this account can put in a token, by Cloudflare's names.</summary>
    Task<List<CloudflarePermissionGroup>> ListPermissionGroupsAsync(string apiToken, string accountId);

    Task<List<CloudflareAccountToken>> ListTokensAsync(string apiToken, string accountId);

    /// <summary>Creates a token; the result carries its <see cref="CloudflareAccountToken.Value"/>.</summary>
    Task<CloudflareAccountToken> CreateTokenAsync(string apiToken, string accountId, string name, IReadOnlyList<CloudflareTokenPolicyRequest> policies, DateTime? expiresOn);

    Task DeleteTokenAsync(string apiToken, string accountId, string tokenId);

    /// <summary>Issues a new value for the same token id; the old value stops working.</summary>
    Task<string> RollTokenAsync(string apiToken, string accountId, string tokenId);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

public sealed class CloudflarePermissionGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Where the group applies, e.g. com.cloudflare.api.account, com.cloudflare.edge.r2.bucket.</summary>
    public List<string> Scopes { get; set; } = new();
}

/// <summary>One allow policy: these groups over these resources (resource key → "*" or a nested map).</summary>
public sealed record CloudflareTokenPolicyRequest(IReadOnlyList<CloudflarePermissionGroup> Groups, IReadOnlyDictionary<string, object> Resources);

public sealed class CloudflareTokenPolicy
{
    public string Effect { get; set; } = "allow";
    public List<string> PermissionGroups { get; set; } = new();
    public List<string> Resources { get; set; } = new();
}

public sealed class CloudflareAccountToken
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? IssuedOn { get; set; }
    public DateTime? ExpiresOn { get; set; }
    public DateTime? LastUsedOn { get; set; }
    public List<CloudflareTokenPolicy> Policies { get; set; } = new();

    /// <summary>Only on create or roll. Never logged, never stored.</summary>
    public string? Value { get; set; }
}

public class CloudflareAccountTokenServiceException : Exception
{
    public CloudflareAccountTokenServiceException(string message) : base(message) { }
    public CloudflareAccountTokenServiceException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class CloudflareAccountTokenService : ICloudflareAccountTokenService
{
    private const string CLOUDFLARE_API_BASE = "https://api.cloudflare.com/client/v4";

    private readonly ILogger<CloudflareAccountTokenService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public CloudflareAccountTokenService(ILogger<CloudflareAccountTokenService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<CloudflarePermissionGroup>> ListPermissionGroupsAsync(string apiToken, string accountId)
    {
        var httpClient = CreateAuthenticatedClient(apiToken);
        var groups = new List<CloudflarePermissionGroup>();
        await foreach (var e in PagesAsync(httpClient, $"{Tokens(accountId)}/permission_groups", "list the permission groups"))
        {
            groups.Add(new CloudflarePermissionGroup
            {
                Id = Str(e, "id") ?? string.Empty,
                Name = Str(e, "name") ?? string.Empty,
                Scopes = e.TryGetProperty("scopes", out var s) && s.ValueKind == JsonValueKind.Array
                    ? s.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                    : new List<string>(),
            });
        }

        return groups;
    }

    public async Task<List<CloudflareAccountToken>> ListTokensAsync(string apiToken, string accountId)
    {
        var httpClient = CreateAuthenticatedClient(apiToken);
        var tokens = new List<CloudflareAccountToken>();
        await foreach (var e in PagesAsync(httpClient, Tokens(accountId), "list the account's API tokens"))
        {
            tokens.Add(ParseToken(e));
        }

        return tokens;
    }

    public async Task<CloudflareAccountToken> CreateTokenAsync(string apiToken, string accountId, string name, IReadOnlyList<CloudflareTokenPolicyRequest> policies, DateTime? expiresOn)
    {
        _logger.LogInformation("Creating the account API token {Name}", name);
        var httpClient = CreateAuthenticatedClient(apiToken);
        var payload = new Dictionary<string, object>
        {
            ["name"] = name,
            ["policies"] = policies.Select(p => new Dictionary<string, object>
            {
                ["effect"] = "allow",
                ["permission_groups"] = p.Groups.Select(g => new Dictionary<string, string> { ["id"] = g.Id, ["name"] = g.Name }).ToList(),
                ["resources"] = p.Resources,
            }).ToList(),
        };
        if (expiresOn is { } expires) payload["expires_on"] = expires.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        var (result, _) = await ReadAsync(await httpClient.PostAsync(Tokens(accountId), JsonBody(payload)), $"create the API token {name}");
        if (result.ValueKind != JsonValueKind.Object)
        {
            throw new CloudflareAccountTokenServiceException($"Cloudflare created the API token {name} but sent no detail back.");
        }

        return ParseToken(result);
    }

    public async Task DeleteTokenAsync(string apiToken, string accountId, string tokenId)
    {
        _logger.LogInformation("Deleting the account API token {TokenId}", tokenId);
        var httpClient = CreateAuthenticatedClient(apiToken);
        await ReadAsync(await httpClient.DeleteAsync($"{Tokens(accountId)}/{Uri.EscapeDataString(tokenId)}"), $"delete the API token {tokenId}");
    }

    public async Task<string> RollTokenAsync(string apiToken, string accountId, string tokenId)
    {
        _logger.LogInformation("Rolling the account API token {TokenId}", tokenId);
        var httpClient = CreateAuthenticatedClient(apiToken);
        var (result, _) = await ReadAsync(await httpClient.PutAsync($"{Tokens(accountId)}/{Uri.EscapeDataString(tokenId)}/value", JsonBody(new { })), $"roll the API token {tokenId}");
        var value = result.ValueKind == JsonValueKind.String ? result.GetString() : Str(result, "value");
        return string.IsNullOrEmpty(value)
            ? throw new CloudflareAccountTokenServiceException($"Cloudflare rolled the API token {tokenId} but sent no new value back.")
            : value;
    }

    // ── helpers ──────────────────────────────────────────────────

    private static string Tokens(string accountId) => $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/tokens";

    private static CloudflareAccountToken ParseToken(JsonElement e) => new()
    {
        Id = Str(e, "id") ?? string.Empty,
        Name = Str(e, "name") ?? string.Empty,
        Status = Str(e, "status") ?? string.Empty,
        IssuedOn = Date(e, "issued_on"),
        ExpiresOn = Date(e, "expires_on"),
        LastUsedOn = Date(e, "last_used_on"),
        Value = Str(e, "value"),
        Policies = e.TryGetProperty("policies", out var policies) && policies.ValueKind == JsonValueKind.Array
            ? policies.EnumerateArray().Select(ParsePolicy).ToList()
            : new List<CloudflareTokenPolicy>(),
    };

    private static CloudflareTokenPolicy ParsePolicy(JsonElement p) => new()
    {
        Effect = Str(p, "effect") ?? "allow",
        PermissionGroups = p.TryGetProperty("permission_groups", out var groups) && groups.ValueKind == JsonValueKind.Array
            ? groups.EnumerateArray().Select(g => Str(g, "name") ?? Str(g, "id") ?? string.Empty).ToList()
            : new List<string>(),
        Resources = p.TryGetProperty("resources", out var resources) && resources.ValueKind == JsonValueKind.Object
            ? resources.EnumerateObject().Select(r => r.Name).ToList()
            : new List<string>(),
    };

    /// <summary>Every element of a paged Cloudflare list (page / total_pages in result_info).</summary>
    private async IAsyncEnumerable<JsonElement> PagesAsync(HttpClient httpClient, string url, string operation)
    {
        var page = 1;
        while (true)
        {
            var (result, info) = await ReadAsync(await httpClient.GetAsync($"{url}?page={page}&per_page=50"), operation);
            if (result.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in result.EnumerateArray()) yield return e;
            }

            var totalPages = info.ValueKind == JsonValueKind.Object && info.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var n) ? n : 1;
            if (page >= totalPages) yield break;
            page++;
        }
    }

    private static string? Str(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? Date(JsonElement e, string property) =>
        Str(e, property) is { } s && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

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
            throw new CloudflareAccountTokenServiceException($"Cloudflare sent an unreadable answer while trying to {operation} ({response.StatusCode}).", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var refused = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False;
            if (!response.IsSuccessStatusCode || refused)
            {
                var detail = ExtractErrorMessage(root);
                _logger.LogWarning("Cloudflare tokens API failed to {Operation}: {StatusCode} {Detail}", operation, response.StatusCode, detail);
                throw new CloudflareAccountTokenServiceException($"Cloudflare could not {operation}: {response.StatusCode} — {detail}");
            }

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
