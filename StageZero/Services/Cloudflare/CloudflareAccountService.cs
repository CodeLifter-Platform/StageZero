using System.Net.Http.Headers;
using System.Text.Json;

namespace StageZero.Services.Cloudflare;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The token-level Cloudflare calls the Settings page needs: is this token alive, and which
/// accounts can it see. Zone, DNS, tunnel and Access calls stay in their own services.
/// </summary>
public interface ICloudflareAccountService
{
    /// <summary>
    /// Cloudflare's verify call. Tries the user-token endpoint, then, when an account is
    /// known, the account-token one (a token created under Manage Account → API Tokens).
    /// </summary>
    Task<CloudflareTokenInfo> VerifyTokenAsync(string apiToken, string? accountId);

    /// <summary>The accounts the token has any account-scoped permission on. Empty for a zone-only token.</summary>
    Task<List<CloudflareAccount>> ListAccountsAsync(string apiToken);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

public class CloudflareTokenInfo
{
    public string Id { get; set; } = string.Empty;

    /// <summary>"active", "disabled" or "expired".</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime? NotBefore { get; set; }
    public DateTime? ExpiresOn { get; set; }
}

public class CloudflareAccount
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class CloudflareAccountServiceException : Exception
{
    public CloudflareAccountServiceException(string message) : base(message) { }
    public CloudflareAccountServiceException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class CloudflareAccountService : ICloudflareAccountService
{
    private readonly ILogger<CloudflareAccountService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private const string CLOUDFLARE_API_BASE = "https://api.cloudflare.com/client/v4";

    public CloudflareAccountService(
        ILogger<CloudflareAccountService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<CloudflareTokenInfo> VerifyTokenAsync(string apiToken, string? accountId)
    {
        var httpClient = CreateAuthenticatedClient(apiToken);
        try
        {
            return ParseToken(await ReadResultAsync(
                await httpClient.GetAsync($"{CLOUDFLARE_API_BASE}/user/tokens/verify"), "verify the token"));
        }
        catch (CloudflareAccountServiceException) when (!string.IsNullOrWhiteSpace(accountId))
        {
            _logger.LogDebug("Not a user token; trying the account-token verify endpoint");
            return ParseToken(await ReadResultAsync(
                await httpClient.GetAsync($"{CLOUDFLARE_API_BASE}/accounts/{accountId}/tokens/verify"), "verify the token"));
        }
    }

    public async Task<List<CloudflareAccount>> ListAccountsAsync(string apiToken)
    {
        var httpClient = CreateAuthenticatedClient(apiToken);
        var result = await ReadResultAsync(
            await httpClient.GetAsync($"{CLOUDFLARE_API_BASE}/accounts?per_page=50"), "list accounts");

        var accounts = new List<CloudflareAccount>();
        if (result.ValueKind != JsonValueKind.Array) return accounts;

        foreach (var account in result.EnumerateArray())
        {
            accounts.Add(new CloudflareAccount
            {
                Id = account.GetProperty("id").GetString() ?? string.Empty,
                Name = account.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty
            });
        }

        return accounts;
    }

    private static CloudflareTokenInfo ParseToken(JsonElement result) => new()
    {
        Id = result.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
        Status = result.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty,
        NotBefore = ReadDate(result, "not_before"),
        ExpiresOn = ReadDate(result, "expires_on")
    };

    private static DateTime? ReadDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed.UtcDateTime
            : null;

    /// <summary>Validates a Cloudflare response and returns a clone of its "result" element.</summary>
    private async Task<JsonElement> ReadResultAsync(HttpResponseMessage response, string operation)
    {
        var responseBody = await response.Content.ReadAsStringAsync();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(responseBody);
        }
        catch (JsonException ex)
        {
            throw new CloudflareAccountServiceException(
                $"Cloudflare sent an unreadable answer while trying to {operation} ({response.StatusCode}).", ex);
        }

        using (doc)
        {
            var succeeded = response.IsSuccessStatusCode
                            && !(doc.RootElement.TryGetProperty("success", out var success)
                                 && success.ValueKind == JsonValueKind.False);
            if (!succeeded)
            {
                var detail = ExtractErrorMessage(doc.RootElement);
                _logger.LogWarning("Cloudflare API failed to {Operation}: {StatusCode} {Detail}",
                    operation, response.StatusCode, detail);
                throw new CloudflareAccountServiceException($"Cloudflare could not {operation}: {detail}");
            }

            return doc.RootElement.TryGetProperty("result", out var result) ? result.Clone() : default;
        }
    }

    private static string ExtractErrorMessage(JsonElement root)
    {
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            var messages = errors.EnumerateArray()
                .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();
            if (messages.Count > 0) return string.Join("; ", messages);
        }

        return "no detail returned";
    }

    private HttpClient CreateAuthenticatedClient(string apiToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        return httpClient;
    }
}
