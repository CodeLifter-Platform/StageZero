using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StageZero.DataAdapters.DnsRecords;
using StageZero.Models;

namespace StageZero.Services.Dns;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface ICloudflareService
{
    Task<bool> UpdateDnsRecordAsync(DnsProvider provider, DnsRecord record, string ipAddress);
    Task<List<CloudflareZone>> GetZonesAsync(string apiToken);
    Task<List<CloudflareDnsRecord>> GetDnsRecordsAsync(string apiToken, string zoneId);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

public class CloudflareZone
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class CloudflareDnsRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class CloudflareServiceException : Exception
{
    public CloudflareServiceException(string message) : base(message) { }
    public CloudflareServiceException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class CloudflareService : ICloudflareService
{
    private readonly ILogger<CloudflareService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDnsRecordWriter _dnsRecordWriter;
    private const string CLOUDFLARE_API_BASE = "https://api.cloudflare.com/client/v4";

    public CloudflareService(
        ILogger<CloudflareService> logger,
        IHttpClientFactory httpClientFactory,
        IDnsRecordWriter dnsRecordWriter)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _dnsRecordWriter = dnsRecordWriter;
    }

    public async Task<bool> UpdateDnsRecordAsync(DnsProvider provider, DnsRecord record, string ipAddress)
    {
        try
        {
            if (string.IsNullOrEmpty(provider.ZoneId))
            {
                throw new CloudflareServiceException("Zone ID is required for Cloudflare provider");
            }

            var httpClient = CreateAuthenticatedClient(provider.ApiToken);

            // If we don't have a record ID, we need to find it first
            if (string.IsNullOrEmpty(record.RecordId))
            {
                _logger.LogDebug("Finding Cloudflare DNS record ID for {RecordName}", record.RecordName);
                var recordId = await FindRecordIdAsync(httpClient, provider.ZoneId, record.RecordName, record.RecordType);
                
                if (recordId == null)
                {
                    _logger.LogWarning("DNS record {RecordName} not found in Cloudflare", record.RecordName);
                    return false;
                }

                record.RecordId = recordId;
                await _dnsRecordWriter.UpdateAsync(record);
            }

            // Update the DNS record
            _logger.LogInformation("Updating Cloudflare DNS record {RecordName} to {IpAddress}", 
                record.RecordName, ipAddress);

            // PATCH with the address alone. A PUT replaces the whole record, and the old one
            // sent proxied = false and ttl = 1, silently switching off Cloudflare's proxy (and
            // any custom TTL) on every record it touched.
            var updateUrl = $"{CLOUDFLARE_API_BASE}/zones/{provider.ZoneId}/dns_records/{record.RecordId}";
            var content = new StringContent(
                JsonSerializer.Serialize(new { content = ipAddress }),
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PatchAsync(updateUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to update Cloudflare DNS record: {Response}", responseBody);
                throw new CloudflareServiceException($"Cloudflare API error: {response.StatusCode}");
            }

            // Update our record
            record.LastIpAddress = ipAddress;
            record.LastUpdatedAt = DateTime.UtcNow;
            await _dnsRecordWriter.UpdateAsync(record);

            _logger.LogInformation("Successfully updated DNS record {RecordName}", record.RecordName);
            return true;
        }
        catch (CloudflareServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Cloudflare DNS record {RecordName}", record.RecordName);
            throw new CloudflareServiceException("Could not update DNS record", ex);
        }
    }

    public async Task<List<CloudflareZone>> GetZonesAsync(string apiToken)
    {
        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);
            return await GetAllPagesAsync(httpClient, $"{CLOUDFLARE_API_BASE}/zones", ZonesPerPage, zone => new CloudflareZone
            {
                Id = zone.GetProperty("id").GetString() ?? "",
                Name = zone.GetProperty("name").GetString() ?? ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Cloudflare zones");
            throw new CloudflareServiceException("Could not get zones", ex);
        }
    }

    public async Task<List<CloudflareDnsRecord>> GetDnsRecordsAsync(string apiToken, string zoneId)
    {
        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);
            return await GetAllPagesAsync(httpClient, $"{CLOUDFLARE_API_BASE}/zones/{zoneId}/dns_records", RecordsPerPage, record => new CloudflareDnsRecord
            {
                Id = record.GetProperty("id").GetString() ?? "",
                Name = record.GetProperty("name").GetString() ?? "",
                Type = record.GetProperty("type").GetString() ?? "",
                Content = record.GetProperty("content").GetString() ?? ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Cloudflare DNS records");
            throw new CloudflareServiceException("Could not get DNS records", ex);
        }
    }

    // Cloudflare's maxima for these two lists. Every page is read: a list stopped at page one
    // silently hid every zone past the 20th and every record past the 100th.
    private const int ZonesPerPage = 50;
    private const int RecordsPerPage = 500;
    private const int MaxPages = 200;

    private static async Task<List<T>> GetAllPagesAsync<T>(
        HttpClient httpClient, string url, int perPage, Func<JsonElement, T> map)
    {
        var items = new List<T>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var response = await httpClient.GetAsync($"{url}?page={page}&per_page={perPage}");
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new CloudflareServiceException($"Cloudflare list failed: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                items.AddRange(result.EnumerateArray().Select(map));
            }

            var totalPages = doc.RootElement.TryGetProperty("result_info", out var info)
                && info.TryGetProperty("total_pages", out var total)
                && total.TryGetInt32(out var pages)
                ? pages
                : 1;
            if (page >= totalPages)
            {
                break;
            }
        }

        return items;
    }

    private async Task<string?> FindRecordIdAsync(HttpClient httpClient, string zoneId, string recordName, string recordType)
    {
        var response = await httpClient.GetAsync(
            $"{CLOUDFLARE_API_BASE}/zones/{zoneId}/dns_records?name={Uri.EscapeDataString(recordName)}&type={Uri.EscapeDataString(recordType)}");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);

        if (doc.RootElement.TryGetProperty("result", out var result))
        {
            var firstRecord = result.EnumerateArray().FirstOrDefault();
            if (firstRecord.ValueKind != JsonValueKind.Undefined)
            {
                return firstRecord.GetProperty("id").GetString();
            }
        }

        return null;
    }

    private HttpClient CreateAuthenticatedClient(string apiToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        return httpClient;
    }
}
