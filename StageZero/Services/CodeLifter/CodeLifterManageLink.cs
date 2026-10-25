using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lifted.BlazorAuth.Basic.Services;
using Microsoft.Extensions.Logging;

namespace StageZero.Services.CodeLifter;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Settings → Manage email preferences: asks codelifter.net
/// (<c>POST /api/subscriptions/manage-link</c>) to email the operator a one-hour link to a
/// page where the newsletter and each app's updates switch on and off. The inbox is the
/// only credential, so StageZero stores nothing for this (CodeLifter.Net, NEWSLETTER.md →
/// Managing lists from an emailed link). Best-effort by contract: it reports, never throws.
/// </summary>
public interface ICodeLifterManageLink
{
    /// <summary>False when the codelifter.net URL is blank (a fork, an air-gapped install).</summary>
    bool Enabled { get; }

    Task<SignupOptInResult> RequestAsync(string email, CancellationToken cancellationToken = default);
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public sealed class CodeLifterManageLink : ICodeLifterManageLink
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CodeLifterSubscriptionsOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CodeLifterManageLink> _logger;

    public CodeLifterManageLink(
        CodeLifterSubscriptionsOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<CodeLifterManageLink> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool Enabled => _options.ManageLinkUrl is not null;

    public async Task<SignupOptInResult> RequestAsync(string email, CancellationToken cancellationToken = default)
    {
        if (_options.ManageLinkUrl is not { } url)
        {
            return new SignupOptInResult(false, "Switched off on this install.");
        }

        try
        {
            using var client = _httpClientFactory.CreateClient(CodeLifterSubscriptions.HttpClientName);
            client.Timeout = Timeout;
            using var content = new StringContent(
                JsonSerializer.Serialize(new ManageLinkRequest(email), Json), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content, cancellationToken);
            var body = await ReadBodyAsync(response, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("codelifter.net declined a manage link for {Email}: HTTP {Status} {Error}",
                    email, (int)response.StatusCode, body?.Error);
                return new SignupOptInResult(false, body?.Error is { Length: > 0 } error
                    ? $"Couldn't send the link: {error}"
                    : $"Couldn't send the link (codelifter.net answered {(int)response.StatusCode}).");
            }

            _logger.LogInformation("Manage link requested for {Email}", email);
            return new SignupOptInResult(true,
                $"Check {email}: the link to manage your emails works for an hour.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Could not reach codelifter.net for a manage link for {Email}", email);
            return new SignupOptInResult(false, "Couldn't reach codelifter.net. Try again later.");
        }
    }

    private static async Task<ManageLinkResponse?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ManageLinkResponse>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The wire shape of <c>POST /api/subscriptions/manage-link</c>.</summary>
    internal sealed record ManageLinkRequest([property: JsonPropertyName("email")] string Email);

    internal sealed record ManageLinkResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("error")] string? Error);
}
