using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Lifted.BlazorAuth.Basic.Services;

// ═══════════════════════════════════════════════════════════════
// THROTTLE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Per-address limits on the guessable operations: signing in, and asking for or entering a
/// password reset code. Each counts failures (for reset: every request too) in a sliding
/// window; past the limit, that address is refused until the oldest one ages out.
/// <para>
/// This is what stops a single address hammering many accounts. The per-account lockout
/// (<c>User.LockoutEndsAt</c>) is the backstop for one account attacked from many.
/// </para>
/// </summary>
public interface IAuthThrottle
{
    /// <summary>How long this address must wait, or null if it may try now.</summary>
    TimeSpan? RetryAfter(AuthThrottleScope scope, string address);

    void RecordFailure(AuthThrottleScope scope, string address);
}

public enum AuthThrottleScope
{
    Login,
    PasswordReset
}

public sealed class AuthThrottle : IAuthThrottle
{
    public const int MaxFailures = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<(AuthThrottleScope, string), Queue<DateTimeOffset>> _failures = new();

    public AuthThrottle(TimeProvider time)
    {
        _time = time;
    }

    public TimeSpan? RetryAfter(AuthThrottleScope scope, string address)
    {
        if (!_failures.TryGetValue((scope, address), out var failures))
        {
            return null;
        }

        lock (failures)
        {
            var now = _time.GetUtcNow();
            Expire(failures, now);
            return failures.Count >= MaxFailures ? failures.Peek() + Window - now : null;
        }
    }

    public void RecordFailure(AuthThrottleScope scope, string address)
    {
        var failures = _failures.GetOrAdd((scope, address), _ => new Queue<DateTimeOffset>());
        lock (failures)
        {
            var now = _time.GetUtcNow();
            Expire(failures, now);
            failures.Enqueue(now);
        }
    }

    private static void Expire(Queue<DateTimeOffset> failures, DateTimeOffset now)
    {
        while (failures.Count > 0 && failures.Peek() + Window <= now)
        {
            failures.Dequeue();
        }
    }
}

/// <summary>Refused because this address has failed too often; try again after <see cref="RetryAfter"/>.</summary>
public sealed class AuthThrottledException : Exception
{
    public AuthThrottledException(TimeSpan retryAfter)
        : base($"Too many attempts. Try again in {Math.Max(1, (int)Math.Ceiling(retryAfter.TotalMinutes))} minute(s).")
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan RetryAfter { get; }
}

// ═══════════════════════════════════════════════════════════════
// CLIENT ADDRESS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The address of whoever is on this circuit, for the throttle. Interactive components have
/// no HttpContext, so it is captured when the circuit opens — from the circuit's own
/// connection request, after forwarded headers are applied.
/// </summary>
public sealed class ClientAddress
{
    public const string Unknown = "unknown";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private string? _captured;

    public ClientAddress(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>The captured address, else the current request's, else <see cref="Unknown"/>
    /// (every unknown caller shares one bucket, which errs toward throttling).</summary>
    public string Value =>
        _captured
        ?? _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        ?? Unknown;

    public void Capture() =>
        _captured ??= _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

public sealed class ClientAddressCircuitHandler : CircuitHandler
{
    private readonly ClientAddress _clientAddress;
    private readonly ILogger<ClientAddressCircuitHandler> _logger;

    public ClientAddressCircuitHandler(ClientAddress clientAddress, ILogger<ClientAddressCircuitHandler> logger)
    {
        _clientAddress = clientAddress;
        _logger = logger;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _clientAddress.Capture();
        _logger.LogDebug("Circuit {CircuitId} opened from {ClientAddress}", circuit.Id, _clientAddress.Value);
        return Task.CompletedTask;
    }
}
