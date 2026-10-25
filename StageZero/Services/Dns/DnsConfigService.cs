using StageZero.DataAdapters.DnsProviders;
using StageZero.DataAdapters.DnsRecords;
using StageZero.Models;
using StageZero.Services.Cloudflare;

namespace StageZero.Services.Dns;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The DNS Configuration screen's operations: Cloudflare providers and the records StageZero
/// tracks for them, reached with the one token saved on the Settings page. Every rule about which records may auto-update lives here (and in
/// <see cref="DnsRecord.SupportsAutoUpdate"/>), so no client can switch it on for a type the
/// verifier would break.
/// </summary>
public interface IDnsConfigService
{
    Task<List<DnsProvider>> GetProvidersAsync();
    Task<DnsProvider> AddProviderAsync(string name, string zoneId, bool isActive);
    Task<DnsProvider> ToggleProviderAsync(int providerId);
    Task DeleteProviderAsync(int providerId);
    Task<DnsRecord> AddRecordAsync(int providerId, DnsRecordInput input);
    Task<DnsRecord> UpdateRecordAsync(int recordId, DnsRecordInput input);
    Task DeleteRecordAsync(int recordId);
    /// <summary>The zones the saved token can see.</summary>
    Task<List<CloudflareZone>> GetCloudflareZonesAsync();
    Task<List<CloudflareDnsRecord>> GetCloudflareRecordsAsync(int providerId);
    Task<DnsImportResult> ImportAllRecordsAsync(int providerId);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

/// <summary>A record as the add and edit forms submit it.</summary>
public sealed record DnsRecordInput(
    string RecordName,
    string RecordType,
    bool AutoUpdate,
    string? RecordId = null,
    string? Content = null);

public sealed record DnsImportResult(int Imported, int Skipped);

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class DnsConfigService : IDnsConfigService
{
    /// <summary>The record types StageZero tracks. Anything else in a zone is left alone.</summary>
    public static readonly IReadOnlySet<string> TrackedTypes = new HashSet<string>(StringComparer.Ordinal) { "A", "AAAA", "CNAME" };

    private readonly ILogger<DnsConfigService> _logger;
    private readonly IDnsProviderReader _providerReader;
    private readonly IDnsProviderWriter _providerWriter;
    private readonly IDnsRecordReader _recordReader;
    private readonly IDnsRecordWriter _recordWriter;
    private readonly ICloudflareService _cloudflareService;
    private readonly ICloudflareCredentialService _credentials;

    public DnsConfigService(
        ILogger<DnsConfigService> logger,
        IDnsProviderReader providerReader,
        IDnsProviderWriter providerWriter,
        IDnsRecordReader recordReader,
        IDnsRecordWriter recordWriter,
        ICloudflareService cloudflareService,
        ICloudflareCredentialService credentials)
    {
        _logger = logger;
        _providerReader = providerReader;
        _providerWriter = providerWriter;
        _recordReader = recordReader;
        _recordWriter = recordWriter;
        _cloudflareService = cloudflareService;
        _credentials = credentials;
    }

    public Task<List<DnsProvider>> GetProvidersAsync() => _providerReader.GetAllAsync();

    public async Task<DnsProvider> AddProviderAsync(string name, string zoneId, bool isActive)
    {
        name = name.Trim();
        zoneId = zoneId.Trim();
        if (name.Length == 0) throw new UserFacingException("Name is required.");
        if (zoneId.Length == 0) throw new UserFacingException("Pick a zone.");
        await _credentials.RequireAsync();

        _logger.LogInformation("Adding Cloudflare provider: {Name}", name);
        var provider = new DnsProvider
        {
            Name = name,
            ProviderType = "Cloudflare",
            ZoneId = zoneId,
            IsActive = isActive
        };

        await _providerWriter.InsertAsync(provider);
        return provider;
    }

    public async Task<DnsProvider> ToggleProviderAsync(int providerId)
    {
        var provider = await RequireProviderAsync(providerId);
        provider.IsActive = !provider.IsActive;
        await _providerWriter.UpdateAsync(provider);

        _logger.LogInformation("Provider {ProviderId} is now {Status}", providerId, provider.IsActive ? "enabled" : "disabled");
        return provider;
    }

    public async Task DeleteProviderAsync(int providerId)
    {
        var provider = await RequireProviderAsync(providerId);
        _logger.LogInformation("Deleting provider: {ProviderId}", providerId);
        await _providerWriter.DeleteAsync(provider);
    }

    public async Task<DnsRecord> AddRecordAsync(int providerId, DnsRecordInput input)
    {
        await RequireProviderAsync(providerId);
        var valid = Validate(input);

        _logger.LogInformation("Adding DNS record: {RecordName} ({RecordType})", valid.RecordName, valid.RecordType);
        var record = new DnsRecord
        {
            DnsProviderId = providerId,
            RecordName = valid.RecordName,
            RecordType = valid.RecordType,
            RecordId = valid.RecordId,
            Content = valid.Content,
            AutoUpdate = valid.AutoUpdate
        };

        await _recordWriter.InsertAsync(record);
        return record;
    }

    public async Task<DnsRecord> UpdateRecordAsync(int recordId, DnsRecordInput input)
    {
        var record = await _recordReader.GetByIdAsync(recordId)
                     ?? throw UserFacingException.NotFound("That DNS record no longer exists.");
        var valid = Validate(input);

        _logger.LogInformation("Updating DNS record: {RecordId}", recordId);
        record.RecordName = valid.RecordName;
        record.RecordType = valid.RecordType;
        record.RecordId = valid.RecordId;
        record.AutoUpdate = valid.AutoUpdate;
        record.Content = valid.Content;

        await _recordWriter.UpdateAsync(record);
        return record;
    }

    public async Task DeleteRecordAsync(int recordId)
    {
        var record = await _recordReader.GetByIdAsync(recordId)
                     ?? throw UserFacingException.NotFound("That DNS record no longer exists.");
        _logger.LogInformation("Deleting DNS record: {RecordId}", recordId);
        await _recordWriter.DeleteAsync(record);
    }

    public async Task<List<CloudflareZone>> GetCloudflareZonesAsync()
    {
        var credentials = await _credentials.RequireAsync();
        try
        {
            return await _cloudflareService.GetZonesAsync(credentials.ApiToken);
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogWarning(ex, "Could not list Cloudflare zones");
            throw new UserFacingException($"Failed to load zones: {ex.Message}", ex);
        }
    }

    public async Task<List<CloudflareDnsRecord>> GetCloudflareRecordsAsync(int providerId)
    {
        var provider = await RequireProviderAsync(providerId);
        var credentials = await _credentials.RequireAsync();
        try
        {
            var records = await _cloudflareService.GetDnsRecordsAsync(credentials.ApiToken, provider.ZoneId!);
            return records.Where(r => TrackedTypes.Contains(r.Type)).ToList();
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogWarning(ex, "Could not list Cloudflare records for provider {ProviderId}", providerId);
            throw new UserFacingException($"Failed to load DNS records: {ex.Message}", ex);
        }
    }

    public async Task<DnsImportResult> ImportAllRecordsAsync(int providerId)
    {
        var provider = await RequireProviderAsync(providerId);
        var credentials = await _credentials.RequireAsync();
        _logger.LogInformation("Importing all DNS records from Cloudflare for provider {ProviderId}", providerId);

        List<CloudflareDnsRecord> cloudflareRecords;
        try
        {
            cloudflareRecords = await _cloudflareService.GetDnsRecordsAsync(credentials.ApiToken, provider.ZoneId!);
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogWarning(ex, "Could not list Cloudflare records for provider {ProviderId}", providerId);
            throw new UserFacingException($"Error importing records: {ex.Message}", ex);
        }

        var existing = provider.DnsRecords.ToList();
        int imported = 0, skipped = 0;

        foreach (var cfRecord in cloudflareRecords.Where(r => TrackedTypes.Contains(r.Type)))
        {
            if (existing.Any(r => r.RecordName == cfRecord.Name && r.RecordType == cfRecord.Type))
            {
                skipped++;
                continue;
            }

            await _recordWriter.InsertAsync(new DnsRecord
            {
                DnsProviderId = providerId,
                RecordName = cfRecord.Name,
                RecordType = cfRecord.Type,
                RecordId = cfRecord.Id,
                LastIpAddress = cfRecord.Type == "CNAME" ? null : cfRecord.Content,
                Content = cfRecord.Type == "CNAME" ? cfRecord.Content : null,
                AutoUpdate = DnsRecord.SupportsAutoUpdate(cfRecord.Type)
            });
            imported++;
        }

        _logger.LogInformation("Imported {ImportedCount} DNS records, skipped {SkippedCount} duplicates", imported, skipped);
        return new DnsImportResult(imported, skipped);
    }

    /// <summary>
    /// Trims, checks the type, requires a CNAME target, and turns auto-update off for any
    /// type that can't take it. Only a CNAME keeps <c>Content</c>.
    /// </summary>
    public static DnsRecordInput Validate(DnsRecordInput input)
    {
        var name = input.RecordName?.Trim() ?? string.Empty;
        var type = input.RecordType?.Trim().ToUpperInvariant() ?? string.Empty;
        var content = input.Content?.Trim();
        var recordId = string.IsNullOrWhiteSpace(input.RecordId) ? null : input.RecordId.Trim();

        if (name.Length == 0) throw new UserFacingException("Record name is required.");
        if (!TrackedTypes.Contains(type)) throw new UserFacingException("Record type must be A, AAAA or CNAME.");
        if (type == "CNAME" && string.IsNullOrEmpty(content)) throw new UserFacingException("CNAME target is required.");

        return new DnsRecordInput(
            name,
            type,
            input.AutoUpdate && DnsRecord.SupportsAutoUpdate(type),
            recordId,
            type == "CNAME" ? content : null);
    }

    private async Task<DnsProvider> RequireProviderAsync(int providerId) =>
        await _providerReader.GetByIdAsync(providerId)
        ?? throw UserFacingException.NotFound("That DNS provider no longer exists.");
}
