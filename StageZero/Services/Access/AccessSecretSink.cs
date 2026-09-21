namespace StageZero.Services.Access;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Hook for sending a freshly minted service token somewhere durable — a secrets vault, a
/// password manager, an internal API. Cloudflare returns a client secret exactly once, so
/// this is the only chance to capture it outside the browser session that triggered it.
///
/// Replace the default registration in Program.cs to plug a vault in:
/// <c>builder.Services.AddScoped&lt;IAccessSecretSink, MyVaultSink&gt;();</c>
///
/// Implementations must not write the secret to the application log or to disk in
/// plaintext, and must let exceptions surface — a sink that fails silently would leave the
/// operator believing the secret was stored.
/// </summary>
public interface IAccessSecretSink
{
    Task StoreAsync(AccessSecretContext context, CancellationToken cancellationToken = default);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

/// <summary>The hostname a token was minted for, and the token itself.</summary>
public class AccessSecretContext
{
    public string Hostname { get; set; } = string.Empty;

    /// <summary>Carries the only copy of the client secret. Never log this whole object.</summary>
    public MintedServiceToken Token { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Default sink: records that a token was minted, without its secret. The secret still
/// reaches the operator through the one-time dialog; nothing else keeps a copy.
/// </summary>
public class LoggingAccessSecretSink : IAccessSecretSink
{
    private readonly ILogger<LoggingAccessSecretSink> _logger;

    public LoggingAccessSecretSink(ILogger<LoggingAccessSecretSink> logger)
    {
        _logger = logger;
    }

    public Task StoreAsync(AccessSecretContext context, CancellationToken cancellationToken = default)
    {
        // Client ID and token name are not secrets; the client secret is, and is never logged.
        _logger.LogInformation(
            "Minted Access service token {TokenName} ({TokenId}) for {Hostname}; client ID {ClientId}. "
            + "The client secret was shown once and is not stored by StageZero.",
            context.Token.Name, context.Token.TokenId, context.Hostname, context.Token.ClientId);

        return Task.CompletedTask;
    }
}
