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

            // One listing per zone per check, however many of its records are tracked.
            foreach (var zone in records
                .Where(r => DnsRecord.SupportsAutoUpdate(r.RecordType) && MatchesAddressFamily(r.RecordType, currentIp))
                .GroupBy(r => r.DnsProviderId))
            {
                await VerifyZoneAsync(zone.First().DnsProvider, zone.ToList(), currentIp);
            }

            _logger.LogDebug("Completed DNS verification");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying DNS records");
            // Don't throw - this is a background verification task
        }
    }

    private static bool MatchesAddressFamily(string recordType, string address) =>
        System.Net.IPAddress.TryParse(address, out var ip) && recordType switch
        {
            "A" => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork,
            "AAAA" => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6,
            _ => false
        };

    private async Task VerifyZoneAsync(DnsProvider provider, List<DnsRecord> records, string currentIp)
    {
        if (provider.ProviderType != "Cloudflare")
        {
            _logger.LogWarning("Unknown DNS provider type: {ProviderType}", provider.ProviderType);
            return;
        }

        List<CloudflareDnsRecord> zone;
        try
        {
            zone = await _cloudflareService.GetDnsRecordsAsync(provider.ApiToken, provider.ZoneId ?? "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not list DNS records for provider {Provider}", provider.Name);
            return;
        }

        foreach (var record in records)
        {
            try
            {
                var cloudflareRecord = zone.FirstOrDefault(r =>
                    string.Equals(r.Name, record.RecordName, StringComparison.OrdinalIgnoreCase)
                    && r.Type == record.RecordType);

                if (cloudflareRecord == null)
                {
                    _logger.LogWarning("DNS record {RecordName} not found in Cloudflare", record.RecordName);
                    continue;
                }

                if (cloudflareRecord.Content == currentIp)
                {
                    _logger.LogDebug("DNS record {RecordName} matches current IP", record.RecordName);
                    continue;
                }

                _logger.LogInformation(
                    "DNS record {RecordName} mismatch - Cloudflare: {CloudflareIp}, Current: {CurrentIp}. Updating to sync with current IP...",
                    record.RecordName, cloudflareRecord.Content, currentIp);

                // The listing has the record's current ID, which may differ from ours if it
                // was deleted and recreated in Cloudflare.
                record.RecordId = cloudflareRecord.Id;
                await _cloudflareService.UpdateDnsRecordAsync(provider, record, currentIp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify DNS record {RecordName}", record.RecordName);
                // Don't throw - we want to continue verifying other records
            }
        }
    }
}
