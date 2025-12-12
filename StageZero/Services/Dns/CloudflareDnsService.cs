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

            var updateUrl = $"{CLOUDFLARE_API_BASE}/zones/{provider.ZoneId}/dns_records/{record.RecordId}";
            var updatePayload = new
            {
                type = record.RecordType,
                name = record.RecordName,
                content = ipAddress,
                ttl = 1, // Auto TTL
                proxied = false
            };

            var content = new StringContent(
                JsonSerializer.Serialize(updatePayload),
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PutAsync(updateUrl, content);
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
            var response = await httpClient.GetAsync($"{CLOUDFLARE_API_BASE}/zones");
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new CloudflareServiceException($"Failed to get zones: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var zones = new List<CloudflareZone>();

            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                foreach (var zone in result.EnumerateArray())
                {
                    zones.Add(new CloudflareZone
                    {
                        Id = zone.GetProperty("id").GetString() ?? "",
                        Name = zone.GetProperty("name").GetString() ?? ""
                    });
                }
            }

            return zones;
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
            var response = await httpClient.GetAsync($"{CLOUDFLARE_API_BASE}/zones/{zoneId}/dns_records");
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new CloudflareServiceException($"Failed to get DNS records: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var records = new List<CloudflareDnsRecord>();

            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                foreach (var record in result.EnumerateArray())
                {
                    records.Add(new CloudflareDnsRecord
                    {
                        Id = record.GetProperty("id").GetString() ?? "",
                        Name = record.GetProperty("name").GetString() ?? "",
                        Type = record.GetProperty("type").GetString() ?? "",
                        Content = record.GetProperty("content").GetString() ?? ""
                    });
                }
            }

            return records;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Cloudflare DNS records");
            throw new CloudflareServiceException("Could not get DNS records", ex);
        }
    }

    private async Task<string?> FindRecordIdAsync(HttpClient httpClient, string zoneId, string recordName, string recordType)
    {
        var response = await httpClient.GetAsync(
            $"{CLOUDFLARE_API_BASE}/zones/{zoneId}/dns_records?name={recordName}&type={recordType}");

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
