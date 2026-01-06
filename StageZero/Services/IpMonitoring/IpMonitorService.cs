using StageZero.DataAdapters.IpChecks;
using StageZero.Models;
using StageZero.Services.Dns;

namespace StageZero.Services.IpMonitoring;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IIpMonitorService
{
    Task<IpCheck> CheckIpAsync();
    Task<IpCheck?> GetCurrentIpAsync();
    event EventHandler<IpChangedEventArgs>? IpChanged;
}

public class IpChangedEventArgs : EventArgs
{
    public string NewIp { get; set; } = string.Empty;
    public string? OldIp { get; set; }
    public IpCheck IpCheck { get; set; } = null!;
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class IpMonitorServiceException : Exception
{
    public IpMonitorServiceException(string message) : base(message) { }
    public IpMonitorServiceException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class IpMonitorService : IIpMonitorService
{
    private readonly ILogger<IpMonitorService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IIpCheckReader _ipCheckReader;
    private readonly IIpCheckWriter _ipCheckWriter;
    private readonly IDnsVerificationService _dnsVerificationService;

    public event EventHandler<IpChangedEventArgs>? IpChanged;

    public IpMonitorService(
        ILogger<IpMonitorService> logger,
        IHttpClientFactory httpClientFactory,
        IIpCheckReader ipCheckReader,
        IIpCheckWriter ipCheckWriter,
        IDnsVerificationService dnsVerificationService)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _ipCheckReader = ipCheckReader;
        _ipCheckWriter = ipCheckWriter;
        _dnsVerificationService = dnsVerificationService;
    }

    public async Task<IpCheck> CheckIpAsync()
    {
        try
        {
            _logger.LogDebug("Checking current IP address from ipify.org");

            // Get current IP from ipify.org
            var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync("https://api.ipify.org");
            var currentIp = response.Trim();

            _logger.LogDebug("Current IP: {IpAddress}", currentIp);

            // Get the last check
            var lastCheck = await _ipCheckReader.GetLatestAsync();
            var isChanged = lastCheck == null || lastCheck.IpAddress != currentIp;

            // Create new check record
            var ipCheck = new IpCheck
            {
                IpAddress = currentIp,
                CheckedAt = DateTime.UtcNow,
                IsChanged = isChanged,
                PreviousIpAddress = isChanged ? lastCheck?.IpAddress : null
            };

            // Save to database
            await _ipCheckWriter.InsertAsync(ipCheck);

            if (isChanged)
            {
                _logger.LogInformation("IP address changed from {OldIp} to {NewIp}",
                    lastCheck?.IpAddress ?? "none", currentIp);

                // Raise event
                IpChanged?.Invoke(this, new IpChangedEventArgs
                {
                    NewIp = currentIp,
                    OldIp = lastCheck?.IpAddress,
                    IpCheck = ipCheck
                });
            }
            else
            {
                _logger.LogDebug("IP address unchanged: {IpAddress}", currentIp);
            }

            // Verify DNS records match current IP (runs on every check)
            // Updates Cloudflare if there's any mismatch (regardless of whether local IP changed)
            await _dnsVerificationService.VerifyAndSyncAllRecordsAsync(currentIp, isChanged);

            return ipCheck;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to check IP address from ipify.org");
            throw new IpMonitorServiceException("Could not retrieve IP address", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking IP address");
            throw new IpMonitorServiceException("Could not check IP address", ex);
        }
    }

    public async Task<IpCheck?> GetCurrentIpAsync()
    {
        return await _ipCheckReader.GetLatestAsync();
    }
}

