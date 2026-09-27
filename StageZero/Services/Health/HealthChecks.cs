using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StageZero.Data;
using StageZero.Services.IpMonitoring;

namespace StageZero.Services.Health;

/// <summary>Unhealthy if the database can't be reached — nothing else works without it.</summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public DatabaseHealthCheck(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The database can't be reached");
    }
}

/// <summary>
/// Degraded — not unhealthy — while the public IP hasn't been confirmed for a while: the
/// app is fine, but DNS may be stale (an outage upstream, the IP sources unreachable).
/// Restarting the container wouldn't help, so Docker should still see it as healthy.
/// </summary>
public sealed class IpMonitorHealthCheck : IHealthCheck
{
    /// <summary>A check runs every few minutes; this long without one confirming is news.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    private readonly IIpMonitorStatus _status;
    private readonly TimeProvider _time;

    public IpMonitorHealthCheck(IIpMonitorStatus status, TimeProvider time)
    {
        _status = status;
        _time = time;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var since = _status.LastSuccessAt ?? _status.StartedAt;
        return Task.FromResult(now - since < StaleAfter
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded(_status.LastSuccessAt is null
                ? "The public IP hasn't been confirmed since startup"
                : $"The public IP was last confirmed {(int)(now - since).TotalMinutes} minutes ago"));
    }
}
