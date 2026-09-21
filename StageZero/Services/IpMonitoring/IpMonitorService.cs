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
    private readonly IPublicIpResolver _resolver;
    private readonly IIpCheckReader _ipCheckReader;
    private readonly IIpCheckWriter _ipCheckWriter;
    private readonly IDnsVerificationService _dnsVerificationService;
    private readonly IIpChangeNotifier _notifier;
    private readonly IIpMonitorStatus _status;

    public IpMonitorService(
        ILogger<IpMonitorService> logger,
        IPublicIpResolver resolver,
        IIpCheckReader ipCheckReader,
        IIpCheckWriter ipCheckWriter,
        IDnsVerificationService dnsVerificationService,
        IIpChangeNotifier notifier,
        IIpMonitorStatus status)
    {
        _logger = logger;
        _resolver = resolver;
        _ipCheckReader = ipCheckReader;
        _ipCheckWriter = ipCheckWriter;
        _dnsVerificationService = dnsVerificationService;
        _notifier = notifier;
        _status = status;
    }

    public async Task<IpCheck> CheckIpAsync()
    {
        try
        {
            var lastCheck = await _ipCheckReader.GetLatestAsync();
            var currentIp = await _resolver.ResolveAsync(lastCheck?.IpAddress);

            _logger.LogDebug("Current IP: {IpAddress}", currentIp);

            var isChanged = lastCheck == null || lastCheck.IpAddress != currentIp;
            var now = DateTime.UtcNow;

            IpCheck ipCheck;
            if (isChanged)
            {
                // A new run starts.
                ipCheck = new IpCheck
                {
                    IpAddress = currentIp,
                    CheckedAt = now,
                    LastConfirmedAt = now,
                    Confirmations = 1,
                    IsChanged = true,
                    PreviousIpAddress = lastCheck?.IpAddress
                };
                await _ipCheckWriter.InsertAsync(ipCheck);
            }
            else
            {
                // Same address: extend the current run rather than add a row.
                ipCheck = lastCheck!;
                ipCheck.LastConfirmedAt = now;
                ipCheck.Confirmations++;
                await _ipCheckWriter.UpdateAsync(ipCheck);
            }

            if (isChanged)
            {
                _logger.LogInformation("IP address changed from {OldIp} to {NewIp}",
                    lastCheck?.IpAddress ?? "none", currentIp);

                _notifier.Publish(new IpChangedEventArgs
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

            // Verify DNS records match current IP (runs on every check). This is what updates
            // Cloudflare after a change, and what repairs a record edited behind our back.
            await _dnsVerificationService.VerifyAndSyncAllRecordsAsync(currentIp, isChanged);

            _status.RecordSuccess();
            return ipCheck;
        }
        catch (PublicIpUnresolvedException ex)
        {
            // Nothing recorded and nothing changed: better a missed check than a wrong answer.
            _logger.LogWarning("Public IP not resolved: {Reason}", ex.Message);
            _status.RecordFailure(ex.Message);
            throw new IpMonitorServiceException("Could not determine the public IP address", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking IP address");
            _status.RecordFailure(ex.Message);
            throw new IpMonitorServiceException("Could not check IP address", ex);
        }
    }

    public async Task<IpCheck?> GetCurrentIpAsync()
    {
        return await _ipCheckReader.GetLatestAsync();
    }
}

