using StageZero.DataAdapters.DnsRecords;
using StageZero.Models;

namespace StageZero.Services.Dns;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IDnsVerificationService
{
    Task VerifyAndSyncAllRecordsAsync(string currentIp, bool ipChanged);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class DnsVerificationService : IDnsVerificationService
{
    private readonly ILogger<DnsVerificationService> _logger;
    private readonly IDnsRecordReader _dnsRecordReader;
    private readonly ICloudflareService _cloudflareService;

    public DnsVerificationService(
        ILogger<DnsVerificationService> logger,
        IDnsRecordReader dnsRecordReader,
        ICloudflareService cloudflareService)
    {
        _logger = logger;
        _dnsRecordReader = dnsRecordReader;
        _cloudflareService = cloudflareService;
    }

    public async Task VerifyAndSyncAllRecordsAsync(string currentIp, bool ipChanged)
    {
        try
        {
            _logger.LogDebug("Verifying DNS records match current IP: {IpAddress} (IP Changed: {IpChanged})", currentIp, ipChanged);

            // Get all auto-update records
            var records = await _dnsRecordReader.GetAutoUpdateRecordsAsync();

            if (records.Count == 0)
            {
                _logger.LogDebug("No DNS records configured for auto-update");
                return;
            }

            _logger.LogDebug("Checking {Count} DNS records", records.Count);

            var updateTasks = new List<Task>();
            foreach (var record in records)
            {
                updateTasks.Add(VerifyAndSyncRecordAsync(record, currentIp, ipChanged));
            }

            await Task.WhenAll(updateTasks);

            _logger.LogDebug("Completed DNS verification");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying DNS records");
            // Don't throw - this is a background verification task
        }
    }

    private async Task VerifyAndSyncRecordAsync(DnsRecord record, string currentIp, bool ipChanged)
    {
        try
        {
            // Skip CNAME records - they point to domain names, not IP addresses
            if (record.RecordType == "CNAME")
            {
                _logger.LogDebug("Skipping {RecordName} - CNAME records are not IP-based", record.RecordName);
                return;
            }

            // Only check A and AAAA records
            if (record.RecordType != "A" && record.RecordType != "AAAA")
            {
                _logger.LogDebug("Skipping {RecordName} - Only A and AAAA records are verified", record.RecordName);
                return;
            }

            // Get the current DNS record from Cloudflare
            if (record.DnsProvider.ProviderType == "Cloudflare")
            {
                var cloudflareRecords = await _cloudflareService.GetDnsRecordsAsync(
                    record.DnsProvider.ApiToken,
                    record.DnsProvider.ZoneId ?? "");

                var cloudflareRecord = cloudflareRecords.FirstOrDefault(r =>
                    r.Name == record.RecordName && r.Type == record.RecordType);

                if (cloudflareRecord == null)
                {
                    _logger.LogWarning("DNS record {RecordName} not found in Cloudflare", record.RecordName);
                    return;
                }

                // Check if the IP matches
                if (cloudflareRecord.Content != currentIp)
                {
                    _logger.LogInformation(
                        "DNS record {RecordName} mismatch - Cloudflare: {CloudflareIp}, Current: {CurrentIp}. Updating to sync with current IP...",
                        record.RecordName, cloudflareRecord.Content, currentIp);

                    // Always update when there's a mismatch and auto-update is enabled
                    await _cloudflareService.UpdateDnsRecordAsync(record.DnsProvider, record, currentIp);
                }
                else
                {
                    _logger.LogDebug("DNS record {RecordName} matches current IP", record.RecordName);
                }
            }
            else
            {
                _logger.LogWarning("Unknown DNS provider type: {ProviderType}", record.DnsProvider.ProviderType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify DNS record {RecordName}", record.RecordName);
            // Don't throw - we want to continue verifying other records
        }
    }
}

