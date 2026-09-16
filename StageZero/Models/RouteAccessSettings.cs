using System.Text.RegularExpressions;

namespace StageZero.Models;

/// <summary>
/// The <c>access</c> section of a tunnel route: how Cloudflare Access should protect the
/// hostname, plus the Cloudflare object IDs StageZero has already provisioned for it.
///
/// Stored as extra columns on the TunnelRoutes row (an EF owned type), so a route and its
/// Access settings are always written and read together.
/// </summary>
public class RouteAccessSettings
{
    /// <summary>Cloudflare's default when an application does not set one.</summary>
    public const string DefaultSessionDuration = "24h";

    // ── Desired configuration ────────────────────────────────────

    /// <summary>
    /// Protection level. New routes default to <see cref="AccessMode.Identity"/>; opting out
    /// is deliberate, never the result of leaving a field blank.
    /// </summary>
    public AccessMode Mode { get; set; } = AccessMode.Identity;

    /// <summary>Allowed email addresses, one per line. Used by the identity policy.</summary>
    public string? AllowedEmails { get; set; }

    /// <summary>Allowed email domains (e.g. "codelifter.net"), one per line.</summary>
    public string? AllowedEmailDomains { get; set; }

    /// <summary>
    /// Identity provider IDs the login screen offers, one per line. Null or empty means
    /// "whatever the account already has" — Cloudflare then allows every configured IdP.
    /// </summary>
    public string? AllowedIdpIds { get; set; }

    /// <summary>
    /// How long an Access session lasts, in Cloudflare's duration format ("24h", "2h45m").
    /// </summary>
    public string? SessionDuration { get; set; } = DefaultSessionDuration;

    // ── Service token ────────────────────────────────────────────

    /// <summary>
    /// Mint a new service token during the next provisioning run instead of reusing one.
    /// Cleared once the token exists, because the secret is only ever returned on creation.
    /// </summary>
    public bool CreateServiceToken { get; set; }

    /// <summary>Name of the service token to create or look up.</summary>
    public string? ServiceTokenName { get; set; }

    /// <summary>Cloudflare's UUID for the attached service token, once it is known.</summary>
    public string? ServiceTokenId { get; set; }

    /// <summary>
    /// Optional expiry for a token StageZero mints, in Cloudflare's duration format or the
    /// special value "forever". Null uses Cloudflare's default of 8760h (one year).
    /// </summary>
    public string? ServiceTokenDuration { get; set; }

    // ── Provisioned state ────────────────────────────────────────

    /// <summary>Cloudflare's UUID for the Access application protecting this hostname.</summary>
    public string? ApplicationId { get; set; }

    /// <summary>Reusable policy allowing the configured emails and domains.</summary>
    public string? IdentityPolicyId { get; set; }

    /// <summary>Reusable non-identity policy allowing the attached service token.</summary>
    public string? ServiceTokenPolicyId { get; set; }

    /// <summary>When Access was last successfully provisioned for this hostname.</summary>
    public DateTime? SyncedAt { get; set; }

    // ── Derived helpers ──────────────────────────────────────────

    public IReadOnlyList<string> AllowedEmailList => SplitList(AllowedEmails);

    public IReadOnlyList<string> AllowedEmailDomainList => SplitList(AllowedEmailDomains);

    public IReadOnlyList<string> AllowedIdpIdList => SplitList(AllowedIdpIds);

    /// <summary>The Access application name StageZero uses for a hostname.</summary>
    public static string ApplicationNameFor(string hostname) => hostname;

    /// <summary>
    /// Policy names are how a re-run finds the policies it created last time, so they are
    /// derived from the hostname and must stay stable.
    /// </summary>
    public static string IdentityPolicyNameFor(string hostname) => $"StageZero {hostname} identity";

    public static string ServiceTokenPolicyNameFor(string hostname) => $"StageZero {hostname} service token";

    /// <summary>
    /// Splits a newline-, comma- or semicolon-separated field into trimmed, de-duplicated,
    /// lower-cased entries. The UI offers a multi-line box; operators paste commas anyway.
    /// </summary>
    public static IReadOnlyList<string> SplitList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim().ToLowerInvariant())
            .Where(entry => entry.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Returns a copy, so editing a form does not mutate a tracked entity.</summary>
    public RouteAccessSettings Clone() => (RouteAccessSettings)MemberwiseClone();

    // ── Validation ───────────────────────────────────────────────

    // Cloudflare durations are a sequence of value+unit pairs: "300ms", "2h45m", "24h".
    private static readonly Regex DurationPattern = new(
        @"^(\d+(?:\.\d+)?(?:ns|us|µs|ms|s|m|h))+$",
        RegexOptions.Compiled);

    // Deliberately loose: Cloudflare is the authority on what it accepts. This only catches
    // the mistakes that would otherwise fail halfway through provisioning.
    private static readonly Regex EmailPattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled);

    private static readonly Regex DomainPattern = new(
        @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$",
        RegexOptions.Compiled);

    /// <summary>
    /// Checks the section is internally consistent before any Cloudflare call is made.
    /// Returns an empty list when the settings are usable.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(SessionDuration) && !DurationPattern.IsMatch(SessionDuration.Trim()))
        {
            errors.Add($"Session duration '{SessionDuration}' is not a Cloudflare duration (for example 30m, 24h, 2h45m).");
        }

        if (Mode == AccessMode.None)
        {
            return errors;
        }

        if (Mode.RequiresIdentityPolicy())
        {
            ValidateIdentity(errors);
        }

        if (Mode.RequiresServiceTokenPolicy())
        {
            ValidateServiceToken(errors);
        }

        return errors;
    }

    private void ValidateIdentity(List<string> errors)
    {
        var emails = AllowedEmailList;
        var domains = AllowedEmailDomainList;

        if (emails.Count == 0 && domains.Count == 0)
        {
            errors.Add("Identity mode needs at least one allowed email address or email domain, "
                       + "otherwise the policy would let nobody in.");
        }

        foreach (var email in emails.Where(e => !EmailPattern.IsMatch(e)))
        {
            errors.Add($"'{email}' is not a valid email address.");
        }

        foreach (var domain in domains)
        {
            if (domain.Contains('@'))
            {
                errors.Add($"'{domain}' looks like an email address; put it under allowed emails instead.");
            }
            else if (!DomainPattern.IsMatch(domain))
            {
                errors.Add($"'{domain}' is not a valid email domain.");
            }
        }
    }

    private void ValidateServiceToken(List<string> errors)
    {
        var hasName = !string.IsNullOrWhiteSpace(ServiceTokenName);
        var hasId = !string.IsNullOrWhiteSpace(ServiceTokenId);

        if (CreateServiceToken && !hasName)
        {
            errors.Add("Creating a service token needs a name.");
        }

        if (!CreateServiceToken && !hasName && !hasId)
        {
            errors.Add("Service token mode needs an existing token's name or ID, or the option to create one.");
        }

        if (!string.IsNullOrWhiteSpace(ServiceTokenDuration))
        {
            var duration = ServiceTokenDuration.Trim();
            if (!string.Equals(duration, "forever", StringComparison.OrdinalIgnoreCase)
                && !DurationPattern.IsMatch(duration))
            {
                errors.Add($"Service token duration '{ServiceTokenDuration}' is not a Cloudflare duration "
                           + "(for example 720h) or the value 'forever'.");
            }
        }
    }
}
