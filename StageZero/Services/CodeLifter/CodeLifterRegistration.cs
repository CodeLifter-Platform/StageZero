using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace StageZero.Services.CodeLifter;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Tells codelifter.net who runs this install: <c>POST /api/app-users/register</c> with the
/// admin's address, the app id and the version. It subscribes the person to nothing and
/// makes the site send nothing (CodeLifter.Net, NEWSLETTER.md → App users). Offered on the
/// first-run page, where the operator can untick it. Best-effort by contract: it reports,
/// never throws.
/// </summary>
public interface ICodeLifterRegistration
{
    /// <summary>False when the codelifter.net URL is blank (a fork, an air-gapped install).</summary>
    bool Enabled { get; }

    Task<bool> RegisterAsync(string email, CancellationToken cancellationToken = default);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public sealed class CodeLifterRegistration : ICodeLifterRegistration
{
    // Setup waits for this, so it is kept short: a slow site must not hold up the admin.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CodeLifterSubscriptionsOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CodeLifterRegistration> _logger;

    public CodeLifterRegistration(
        CodeLifterSubscriptionsOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<CodeLifterRegistration> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool Enabled => _options.RegisterUrl is not null;

    public async Task<bool> RegisterAsync(string email, CancellationToken cancellationToken = default)
    {
        if (_options.RegisterUrl is not { } url) return false;

        try
        {
            using var client = _httpClientFactory.CreateClient(CodeLifterSubscriptions.HttpClientName);
            client.Timeout = Timeout;
            using var content = new StringContent(
                JsonSerializer.Serialize(new RegisterRequest(email, _options.AppId, CodeLifterSubscriptions.AppVersion()), Json),
                Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Registered this install's admin {Email} with codelifter.net", email);
                return true;
            }

            _logger.LogWarning("codelifter.net declined to register {Email}: HTTP {Status}", email, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Anything at all: a courtesy call must never fail setup.
            _logger.LogWarning(ex, "Could not reach codelifter.net to register {Email}", email);
            return false;
        }
    }

    /// <summary>The wire shape of <c>POST /api/app-users/register</c>.</summary>
    internal sealed record RegisterRequest(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("app")] string App,
        [property: JsonPropertyName("appVersion")] string? AppVersion);
}
