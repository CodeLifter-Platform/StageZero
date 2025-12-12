using StageZero.DataAdapters.DnsRecords;
using StageZero.Models;
using StageZero.Services.IpMonitoring;

namespace StageZero.Services.Dns;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IDnsUpdateService
{
    Task UpdateAllRecordsAsync(string ipAddress);
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class DnsUpdateServiceException : Exception
{
    public DnsUpdateServiceException(string message) : base(message) { }
    public DnsUpdateServiceException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class DnsUpdateService : IDnsUpdateService
{
    private readonly ILogger<DnsUpdateService> _logger;
    private readonly IDnsRecordReader _dnsRecordReader;
    private readonly ICloudflareService _cloudflareService;

    public DnsUpdateService(
        ILogger<DnsUpdateService> logger,
        IDnsRecordReader dnsRecordReader,
        ICloudflareService cloudflareService)
    {
        _logger = logger;
        _dnsRecordReader = dnsRecordReader;
        _cloudflareService = cloudflareService;
    }

    public async Task UpdateAllRecordsAsync(string ipAddress)
    {
        try
        {
            _logger.LogInformation("Updating all DNS records to IP: {IpAddress}", ipAddress);

            var records = await _dnsRecordReader.GetAutoUpdateRecordsAsync();
            
            if (records.Count == 0)
            {
                _logger.LogInformation("No DNS records configured for auto-update");
                return;
            }

            _logger.LogInformation("Found {Count} DNS records to update", records.Count);

            var updateTasks = new List<Task>();
            foreach (var record in records)
            {
                updateTasks.Add(UpdateRecordAsync(record, ipAddress));
            }

            await Task.WhenAll(updateTasks);

            _logger.LogInformation("Completed updating all DNS records");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating DNS records");
            throw new DnsUpdateServiceException("Could not update DNS records", ex);
        }
    }

    private async Task UpdateRecordAsync(DnsRecord record, string ipAddress)
    {
        try
        {
            // Skip CNAME records - they point to domain names, not IP addresses
            if (record.RecordType == "CNAME")
            {
                _logger.LogDebug("Skipping {RecordName} - CNAME records are not updated with IP addresses", record.RecordName);
                return;
            }

            // Skip if IP hasn't changed
            if (record.LastIpAddress == ipAddress)
            {
                _logger.LogDebug("Skipping {RecordName} - IP unchanged", record.RecordName);
                return;
            }

            _logger.LogInformation("Updating DNS record {RecordName} from {OldIp} to {NewIp}",
                record.RecordName, record.LastIpAddress ?? "none", ipAddress);

            // Route to appropriate provider
            if (record.DnsProvider.ProviderType == "Cloudflare")
            {
                await _cloudflareService.UpdateDnsRecordAsync(record.DnsProvider, record, ipAddress);
            }
            else
            {
                _logger.LogWarning("Unknown DNS provider type: {ProviderType}", record.DnsProvider.ProviderType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update DNS record {RecordName}", record.RecordName);
            // Don't throw - we want to continue updating other records
        }
    }
}

