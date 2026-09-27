using Microsoft.Extensions.Configuration;

namespace StageZero.Services.CodeLifter;

/// <summary>
/// Where the optional first-run signups go. Read from the <c>CodeLifter</c> configuration
/// section (<c>CodeLifter__SubscriptionsUrl</c> in <c>.env</c>). A blank URL turns the
/// boxes off entirely, for a fork or an air-gapped install.
/// </summary>
public sealed class CodeLifterSubscriptionsOptions
{
    public const string SectionName = "CodeLifter";
    public const string DefaultSubscriptionsUrl = "https://codelifter.net/api/subscriptions";
    public const string DefaultAppId = "stagezero";

    /// <summary>The subscriptions endpoint. Blank disables the opt-ins.</summary>
    public string? SubscriptionsUrl { get; init; } = DefaultSubscriptionsUrl;

    /// <summary>This app's id in the codelifter.net registry.</summary>
    public string AppId { get; init; } = DefaultAppId;

    public bool Enabled => Uri.TryCreate(SubscriptionsUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public static CodeLifterSubscriptionsOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var url = section["SubscriptionsUrl"];
        var appId = section["AppId"]?.Trim();

        return new CodeLifterSubscriptionsOptions
        {
            // Absent means the default; present-but-blank means off.
            SubscriptionsUrl = url is null ? DefaultSubscriptionsUrl : url.Trim(),
            AppId = string.IsNullOrEmpty(appId) ? DefaultAppId : appId,
        };
    }
}
