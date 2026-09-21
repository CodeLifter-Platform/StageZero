namespace StageZero.Services.IpMonitoring;

/// <summary>
/// How the IP monitor is doing, in memory: when it started, when it last tried, when it last
/// succeeded, and why the last attempt failed. Read by the health check.
/// </summary>
public interface IIpMonitorStatus
{
    DateTimeOffset StartedAt { get; }
    DateTimeOffset? LastAttemptAt { get; }
    DateTimeOffset? LastSuccessAt { get; }
    string? LastError { get; }

    void RecordSuccess();
    void RecordFailure(string error);
}

public sealed class IpMonitorStatus : IIpMonitorStatus
{
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();

    public IpMonitorStatus(TimeProvider time)
    {
        _time = time;
        StartedAt = time.GetUtcNow();
    }

    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public string? LastError { get; private set; }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            LastAttemptAt = LastSuccessAt = _time.GetUtcNow();
            LastError = null;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_lock)
        {
            LastAttemptAt = _time.GetUtcNow();
            LastError = error;
        }
    }
}
