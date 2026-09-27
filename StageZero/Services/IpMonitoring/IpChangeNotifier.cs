namespace StageZero.Services.IpMonitoring;

/// <summary>
/// App-wide news that the public IP changed. A singleton, so a check made in one scope (the
/// background monitor's) reaches listeners in every other (each circuit's header chip and IP
/// Monitor page). An event on the scoped IpMonitorService only ever reached listeners that
/// happened to hold that same instance — which the background service's never were.
/// <para>
/// Listeners are called on the checking thread and must marshal to their own context. A
/// scoped listener must unsubscribe when disposed, or the singleton keeps it alive.
/// </para>
/// </summary>
public interface IIpChangeNotifier
{
    event EventHandler<IpChangedEventArgs>? IpChanged;

    void Publish(IpChangedEventArgs change);
}

public sealed class IpChangeNotifier : IIpChangeNotifier
{
    private readonly ILogger<IpChangeNotifier> _logger;

    public IpChangeNotifier(ILogger<IpChangeNotifier> logger)
    {
        _logger = logger;
    }

    public event EventHandler<IpChangedEventArgs>? IpChanged;

    public void Publish(IpChangedEventArgs change)
    {
        // One listener throwing must not stop the rest, or the check that published.
        foreach (var listener in IpChanged?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<IpChangedEventArgs>)listener)(this, change);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An IP-change listener failed");
            }
        }
    }
}
