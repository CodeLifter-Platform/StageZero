using System.ComponentModel;
using System.Runtime.CompilerServices;
using StageZero.DataAdapters.DnsProviders;
using StageZero.DataAdapters.DnsRecords;
using StageZero.Models;
using StageZero.Services.Dns;

namespace StageZero.Application.Areas.DnsConfig;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IDnsConfigViewModel : INotifyPropertyChanged
{
    List<DnsProvider> Providers { get; }
    bool IsLoading { get; }
    Task OnInitializedAsync();
    Task AddProviderAsync(string name, string apiToken, string zoneId);
    Task DeleteProviderAsync(int providerId);
    Task AddRecordAsync(int providerId, string recordName, string recordType, string? recordId = null);
    Task UpdateRecordAsync(int recordId, string recordName, string recordType, string? recordIdValue, bool autoUpdate, string? content);
    Task DeleteRecordAsync(int recordId);
    Task<List<CloudflareZone>> GetCloudflareZonesAsync(string apiToken);
    Task<List<CloudflareDnsRecord>> GetCloudflareRecordsAsync(int providerId);
    Task ImportAllRecordsAsync(int providerId);
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class DnsConfigViewModelException : Exception
{
    public DnsConfigViewModelException(string message) : base(message) { }
    public DnsConfigViewModelException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class DnsConfigViewModel : IDnsConfigViewModel
{
    private readonly ILogger<DnsConfigViewModel> _logger;
    private readonly IDnsProviderReader _providerReader;
    private readonly IDnsProviderWriter _providerWriter;
    private readonly IDnsRecordReader _recordReader;
    private readonly IDnsRecordWriter _recordWriter;
    private readonly ICloudflareService _cloudflareService;
    private List<DnsProvider> _providers = new();
    private bool _isLoading;

    public DnsConfigViewModel(
        ILogger<DnsConfigViewModel> logger,
        IDnsProviderReader providerReader,
        IDnsProviderWriter providerWriter,
        IDnsRecordReader recordReader,
        IDnsRecordWriter recordWriter,
        ICloudflareService cloudflareService)
    {
        _logger = logger;
        _providerReader = providerReader;
        _providerWriter = providerWriter;
        _recordReader = recordReader;
        _recordWriter = recordWriter;
        _cloudflareService = cloudflareService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<DnsProvider> Providers
    {
        get => _providers;
        private set => SetProperty(ref _providers, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public async Task OnInitializedAsync()
    {
        try
        {
            IsLoading = true;
            _logger.LogDebug("Initializing DnsConfigViewModel");

            await LoadProvidersAsync();

            _logger.LogInformation("DnsConfigViewModel initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize DnsConfigViewModel");
            throw new DnsConfigViewModelException("Could not initialize DNS config page", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task AddProviderAsync(string name, string apiToken, string zoneId, bool isEnabled = true)
    {
        try
        {
            _logger.LogInformation("Adding Cloudflare provider: {Name}", name);

            var provider = new DnsProvider
            {
                Name = name,
                ProviderType = "Cloudflare",
                ApiToken = apiToken,
                ZoneId = zoneId,
                IsActive = isEnabled
            };

            await _providerWriter.InsertAsync(provider);
            await LoadProvidersAsync();

            _logger.LogInformation("Provider added successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add provider");
            throw new DnsConfigViewModelException("Could not add DNS provider", ex);
        }
    }

    public async Task ToggleProviderAsync(int providerId)
    {
        try
        {
            _logger.LogInformation("Toggling provider status: {ProviderId}", providerId);

            var provider = await _providerReader.GetByIdAsync(providerId);
            if (provider != null)
            {
                provider.IsActive = !provider.IsActive;
                await _providerWriter.UpdateAsync(provider);
                await LoadProvidersAsync();

                _logger.LogInformation("Provider {ProviderId} is now {Status}", providerId, provider.IsActive ? "enabled" : "disabled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle provider");
            throw new DnsConfigViewModelException("Could not toggle DNS provider", ex);
        }
    }

    public async Task DeleteProviderAsync(int providerId)
    {
        try
        {
            _logger.LogInformation("Deleting provider: {ProviderId}", providerId);

            var provider = await _providerReader.GetByIdAsync(providerId);
            if (provider != null)
            {
                await _providerWriter.DeleteAsync(provider);
                await LoadProvidersAsync();
            }

            _logger.LogInformation("Provider deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete provider");
            throw new DnsConfigViewModelException("Could not delete DNS provider", ex);
        }
    }

    public async Task AddRecordAsync(int providerId, string recordName, string recordType, string? recordId = null)
    {
        try
        {
            _logger.LogInformation("Adding DNS record: {RecordName}", recordName);

            var record = new DnsRecord
            {
                DnsProviderId = providerId,
                RecordName = recordName,
                RecordType = recordType,
                RecordId = recordId,
                AutoUpdate = true
            };

            await _recordWriter.InsertAsync(record);
            await LoadProvidersAsync();

            _logger.LogInformation("DNS record added successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add DNS record");
            throw new DnsConfigViewModelException("Could not add DNS record", ex);
        }
    }

    public async Task UpdateRecordAsync(int recordId, string recordName, string recordType, string? recordIdValue, bool autoUpdate, string? content)
    {
        try
        {
            _logger.LogInformation("Updating DNS record: {RecordId}", recordId);

            var record = await _recordReader.GetByIdAsync(recordId);
            if (record != null)
            {
                record.RecordName = recordName;
                record.RecordType = recordType;
                record.RecordId = recordIdValue;
                record.AutoUpdate = autoUpdate;
                record.Content = content;

                await _recordWriter.UpdateAsync(record);
                await LoadProvidersAsync();
            }

            _logger.LogInformation("DNS record updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update DNS record");
            throw new DnsConfigViewModelException("Could not update DNS record", ex);
        }
    }

    public async Task DeleteRecordAsync(int recordId)
    {
        try
        {
            _logger.LogInformation("Deleting DNS record: {RecordId}", recordId);

            var record = await _recordReader.GetByIdAsync(recordId);
            if (record != null)
            {
                await _recordWriter.DeleteAsync(record);
                await LoadProvidersAsync();
            }

            _logger.LogInformation("DNS record deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete DNS record");
            throw new DnsConfigViewModelException("Could not delete DNS record", ex);
        }
    }

    public async Task<List<CloudflareZone>> GetCloudflareZonesAsync(string apiToken)
    {
        return await _cloudflareService.GetZonesAsync(apiToken);
    }

    public async Task<List<CloudflareDnsRecord>> GetCloudflareRecordsAsync(int providerId)
    {
        try
        {
            var provider = await _providerReader.GetByIdAsync(providerId);
            if (provider == null)
            {
                throw new DnsConfigViewModelException("Provider not found");
            }

            return await _cloudflareService.GetDnsRecordsAsync(provider.ApiToken, provider.ZoneId!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Cloudflare records");
            throw new DnsConfigViewModelException("Could not get Cloudflare records", ex);
        }
    }

    public async Task ImportAllRecordsAsync(int providerId)
    {
        try
        {
            _logger.LogInformation("Importing all DNS records from Cloudflare for provider {ProviderId}", providerId);

            var provider = await _providerReader.GetByIdAsync(providerId);
            if (provider == null)
            {
                throw new DnsConfigViewModelException("Provider not found");
            }

            var cloudflareRecords = await _cloudflareService.GetDnsRecordsAsync(provider.ApiToken, provider.ZoneId!);

            // Filter to only A, AAAA, and CNAME records
            var recordsToImport = cloudflareRecords
                .Where(r => r.Type == "A" || r.Type == "AAAA" || r.Type == "CNAME")
                .ToList();

            _logger.LogInformation("Found {Count} A/AAAA/CNAME records to import", recordsToImport.Count);

            // Get existing records to avoid duplicates
            var existingRecords = provider.DnsRecords.ToList();

            int importedCount = 0;
            foreach (var cfRecord in recordsToImport)
            {
                // Check if we already have this record
                if (existingRecords.Any(r => r.RecordName == cfRecord.Name && r.RecordType == cfRecord.Type))
                {
                    _logger.LogDebug("Skipping {RecordName} - already exists", cfRecord.Name);
                    continue;
                }

                var record = new DnsRecord
                {
                    DnsProviderId = providerId,
                    RecordName = cfRecord.Name,
                    RecordType = cfRecord.Type,
                    RecordId = cfRecord.Id,
                    LastIpAddress = cfRecord.Type == "CNAME" ? null : cfRecord.Content,
                    Content = cfRecord.Type == "CNAME" ? cfRecord.Content : null,
                    AutoUpdate = cfRecord.Type != "CNAME" // Don't auto-update CNAME records
                };

                await _recordWriter.InsertAsync(record);
                importedCount++;
            }

            await LoadProvidersAsync();

            _logger.LogInformation("Imported {Count} DNS records successfully", importedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import DNS records");
            throw new DnsConfigViewModelException("Could not import DNS records", ex);
        }
    }

    private async Task LoadProvidersAsync()
    {
        Providers = await _providerReader.GetAllAsync();
    }

    // --- Property Change Support ---

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

