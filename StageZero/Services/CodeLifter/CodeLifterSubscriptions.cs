using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lifted.BlazorAuth.Basic.Services;
using Microsoft.Extensions.Logging;

namespace StageZero.Services.CodeLifter;

/// <summary>
/// The two optional boxes on the first-run setup form, and the one request they turn
/// into: <c>POST /api/subscriptions</c> on codelifter.net. The endpoint is double opt-in,
/// so nothing is subscribed until the person clicks the confirmation email; this class
/// only asks for that email to be sent. Best-effort by contract: it reports, never throws.
/// </summary>
public sealed class CodeLifterSubscriptions : ISignupOptIn
{
    public const string NewsletterKey = "newsletter";
    public const string AppUpdatesKey = "app-updates";
    public const string HttpClientName = "codelifter-subscriptions";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CodeLifterSubscriptionsOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CodeLifterSubscriptions> _logger;

    public CodeLifterSubscriptions(
        CodeLifterSubscriptionsOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<CodeLifterSubscriptions> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        Choices = options.Enabled
            ? new[]
            {
                new SignupOptInChoice(
                    NewsletterKey,
                    "Send me the CodeLifter newsletter",
                    "Occasional posts from codelifter.net. One confirmation email; unsubscribe any time."),
                new SignupOptInChoice(
                    AppUpdatesKey,
                    "Tell me about StageZero updates",
                    "New releases and what changed. Same confirmation email."),
            }
            : Array.Empty<SignupOptInChoice>();
    }

    public IReadOnlyList<SignupOptInChoice> Choices { get; }

    public async Task<SignupOptInResult> SubmitAsync(
        string email, IReadOnlyCollection<string> selectedKeys, CancellationToken cancellationToken = default)
    {
        var newsletter = selectedKeys.Contains(NewsletterKey);
        var appUpdates = selectedKeys.Contains(AppUpdatesKey);
        if (!_options.Enabled || (!newsletter && !appUpdates))
        {
            return new SignupOptInResult(false, "Nothing selected.");
        }

        var request = new SubscriptionRequest(
            email, newsletter, _options.AppId, appUpdates, _options.AppId, AppVersion());

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = Timeout;

            // Buffered with a Content-Length rather than PostAsJsonAsync's chunked
            // stream: one less thing for a proxy between here and the site to get wrong.
            using var content = new StringContent(
                JsonSerializer.Serialize(request, Json), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(_options.SubscriptionsUrl, content, cancellationToken);

            var body = await ReadBodyAsync(response, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "codelifter.net subscriptions declined the signup for {Email}: HTTP {Status} {Error}",
                    email, (int)response.StatusCode, body?.Error);
                return new SignupOptInResult(false, body?.Error is { Length: > 0 } error
                    ? $"Couldn't sign you up: {error}"
                    : $"Couldn't sign you up (codelifter.net answered {(int)response.StatusCode}).");
            }

            if (body?.Status == "pending")
            {
                _logger.LogInformation("Subscription confirmation requested for {Email}: {Confirming}", email, body.Confirming);
                return new SignupOptInResult(true, body.Confirming is { Length: > 0 }
                    ? $"Check {email} to confirm {body.Confirming}."
                    : body.Message ?? "Check your inbox to confirm.");
            }

            // Already subscribed, or the newsletter is managed from a codelifter.net account.
            return new SignupOptInResult(false, body?.Message ?? "Already subscribed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Could not reach codelifter.net to sign {Email} up", email);
            return new SignupOptInResult(false,
                "Couldn't reach codelifter.net to sign you up. Your account is fine; you can subscribe at codelifter.net later.");
        }
    }

    private static async Task<SubscriptionResponse?> ReadBodyAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<SubscriptionResponse>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The build's version without the commit suffix, so the site can tell releases apart.</summary>
    internal static string? AppVersion()
    {
        var informational = typeof(CodeLifterSubscriptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? typeof(CodeLifterSubscriptions).Assembly.GetName().Version?.ToString();
        if (string.IsNullOrEmpty(version)) return null;

        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }

    /// <summary>The wire shape of <c>POST /api/subscriptions</c> (CodeLifter.Net, NEWSLETTER.md).</summary>
    internal sealed record SubscriptionRequest(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("newsletter")] bool Newsletter,
        [property: JsonPropertyName("app")] string App,
        [property: JsonPropertyName("appUpdates")] bool AppUpdates,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("appVersion")] string? AppVersion);

    internal sealed record SubscriptionResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("confirming")] string? Confirming,
        [property: JsonPropertyName("error")] string? Error);
}
