namespace StageZero.Services.Database;

/// <summary>
/// Stops the app so it comes back on a new configuration. The database provider is wired into
/// the host at startup, so a change to it takes a restart; this is the one place that asks
/// for one.
/// </summary>
public interface IAppRestart
{
    /// <summary>A stop has been asked for and is about to happen.</summary>
    bool Pending { get; }

    /// <summary>Stops the host shortly, after the current response has gone out. Idempotent.</summary>
    void Request(string reason);
}

public sealed class AppRestart : IAppRestart
{
    /// <summary>Long enough for the response that announced the restart to reach the browser.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);

    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<AppRestart> _logger;
    private readonly TimeSpan _delay;
    private int _pending;

    public AppRestart(IHostApplicationLifetime lifetime, ILogger<AppRestart> logger)
        : this(lifetime, logger, DefaultDelay)
    {
    }

    public AppRestart(IHostApplicationLifetime lifetime, ILogger<AppRestart> logger, TimeSpan delay)
    {
        _lifetime = lifetime;
        _logger = logger;
        _delay = delay;
    }

    public bool Pending => Volatile.Read(ref _pending) == 1;

    public void Request(string reason)
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1)
        {
            return;
        }

        _logger.LogWarning(
            "Stopping StageZero to {Reason}. Under Docker the container restarts on its own (restart: unless-stopped); " +
            "anywhere else, start StageZero again",
            reason);

        _ = Task.Run(async () =>
        {
            await Task.Delay(_delay);
            _lifetime.StopApplication();
        });
    }
}
