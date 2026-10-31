using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Services.Database;

namespace StageZero.Tests.Database;

public sealed class AppRestartTests
{
    [Fact]
    public async Task A_requested_restart_stops_the_host_once()
    {
        var lifetime = new FakeLifetime();
        var restart = new AppRestart(lifetime, NullLogger<AppRestart>.Instance, TimeSpan.Zero);

        restart.Request("switch to PostgreSQL");
        restart.Request("switch to PostgreSQL");

        Assert.True(restart.Pending);
        await lifetime.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        Assert.Equal(1, lifetime.StopCalls);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public TaskCompletionSource Stopped { get; } = new();
        public int StopCalls;

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            Interlocked.Increment(ref StopCalls);
            Stopped.TrySetResult();
        }
    }
}
