using StageZero.DataAdapters.CloudflareCredentials;
using StageZero.DataAdapters.TunnelConfigs;
using StageZero.Models;
using StageZero.Services.Access;
using StageZero.Services.Dns;
using StageZero.Services.Tunnel;

namespace StageZero.Services.Cloudflare;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The one Cloudflare API token StageZero uses for DNS, the tunnel and Access, and the account
/// it reaches. Added, checked, replaced and removed on the Settings page; everything else
/// asks for it here and never takes a token from a request.
/// </summary>
public interface ICloudflareCredentialService
{
    /// <summary>What is saved, without the token.</summary>
    Task<CloudflareCredentialStatus> GetStatusAsync();

    /// <summary>
    /// Verifies the token with Cloudflare, works out its account, saves it encrypted (replacing
    /// any saved token), and reports which of <see cref="RequiredPermissions"/> it holds.
    /// </summary>
    Task<CloudflareCredentialCheck> SaveAsync(string apiToken, string? accountId);

    /// <summary>Re-verifies the saved token and re-runs the permission checks.</summary>
    Task<CloudflareCredentialCheck> CheckAsync();

    /// <summary>Forgets the saved token. It keeps working at Cloudflare until it is deleted there.</summary>
    Task RemoveAsync();

    /// <summary>The token in the clear and its account, or null if none is saved or it can't be decrypted.</summary>
    Task<CloudflareCredentials?> ResolveAsync();

    /// <summary>
    /// <see cref="ResolveAsync"/>, or a 409 the operator can act on. With
    /// <paramref name="needsAccount"/>, a token with no account is refused too.
    /// </summary>
    Task<CloudflareCredentials> RequireAsync(bool needsAccount = false);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record CloudflareCredentials(string ApiToken, string? AccountId);

/// <param name="IsReadable">False when a token is saved but the data-protection keys that decrypt it are gone.</param>
public sealed record CloudflareCredentialStatus(
    bool IsConfigured,
    bool IsReadable,
    string? AccountId,
    string? AccountName,
    string? TokenId,
    DateTime? ExpiresOn,
    DateTime? VerifiedAt,
    DateTime? UpdatedAt)
{
    public static readonly CloudflareCredentialStatus None = new(false, false, null, null, null, null, null, null);
}

/// <summary>One permission the token needs, phrased as Cloudflare's Create Token page shows it.</summary>
public sealed record CloudflarePermission(string Scope, string Item, string Level, string UsedFor);

/// <param name="Detail">What the check saw, for the operator.</param>
public sealed record CloudflarePermissionResult(CloudflarePermission Permission, bool Granted, string Detail);

public sealed record CloudflareCredentialCheck(
    CloudflareCredentialStatus Status, IReadOnlyList<CloudflarePermissionResult> Permissions);

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class CloudflareCredentialService : ICloudflareCredentialService
{
    public static readonly CloudflarePermission TunnelEdit =
        new("Account", "Cloudflare Tunnel", "Edit", "Create or adopt the tunnel and push its routes");
    public static readonly CloudflarePermission AccessAppsEdit =
        new("Account", "Access: Apps and Policies", "Edit", "Put Cloudflare Access in front of tunnel routes");
    public static readonly CloudflarePermission AccessServiceTokensEdit =
        new("Account", "Access: Service Tokens", "Edit", "Mint service tokens for machine access to routes");
    public static readonly CloudflarePermission DnsEdit =
        new("Zone", "DNS", "Edit", "Keep records on your public IP and write tunnel CNAMEs");
    public static readonly CloudflarePermission ZoneRead =
        new("Zone", "Zone", "Read", "List the zones to manage");

    /// <summary>Everything StageZero does with one token. The Settings page shows this table.</summary>
    public static readonly IReadOnlyList<CloudflarePermission> RequiredPermissions =
        [TunnelEdit, AccessAppsEdit, AccessServiceTokensEdit, DnsEdit, ZoneRead];

    public const string MissingMessage =
        "No Cloudflare API token is saved. Add one on the Settings page (open your email at the top right).";

    private const string UnreadableMessage =
        "The saved Cloudflare API token can't be decrypted: the data-protection keys it was saved with are gone. "
        + "Enter the token again on the Settings page.";

    private readonly ILogger<CloudflareCredentialService> _logger;
    private readonly ICloudflareCredentialReader _reader;
    private readonly ICloudflareCredentialWriter _writer;
    private readonly ICloudflareTokenProtector _protector;
    private readonly ICloudflareAccountService _accounts;
    private readonly ICloudflareService _dns;
    private readonly ICloudflareTunnelService _tunnels;
    private readonly ICloudflareAccessService _access;
    private readonly ITunnelConfigReader _tunnelConfig;
    private readonly TimeProvider _time;

    public CloudflareCredentialService(
        ILogger<CloudflareCredentialService> logger,
        ICloudflareCredentialReader reader,
        ICloudflareCredentialWriter writer,
        ICloudflareTokenProtector protector,
        ICloudflareAccountService accounts,
        ICloudflareService dns,
        ICloudflareTunnelService tunnels,
        ICloudflareAccessService access,
        ITunnelConfigReader tunnelConfig,
        TimeProvider time)
    {
        _logger = logger;
        _reader = reader;
        _writer = writer;
        _protector = protector;
        _accounts = accounts;
        _dns = dns;
        _tunnels = tunnels;
        _access = access;
        _tunnelConfig = tunnelConfig;
        _time = time;
    }

    public async Task<CloudflareCredentialStatus> GetStatusAsync()
    {
        var saved = await _reader.GetAsync();
        return saved is null ? CloudflareCredentialStatus.None : ToStatus(saved, Reveal(saved) is not null);
    }

    public async Task<CloudflareCredentialCheck> SaveAsync(string apiToken, string? accountId)
    {
        apiToken = apiToken?.Trim() ?? string.Empty;
        accountId = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim();
        if (apiToken.Length == 0) throw new UserFacingException("API token is required.");

        var token = await VerifyAsync(apiToken, accountId);
        var account = await ResolveAccountAsync(apiToken, accountId);
        await RequireSameTunnelAccountAsync(account?.Id);

        var now = _time.GetUtcNow().UtcDateTime;
        var saved = await _writer.UpsertAsync(new CloudflareCredential
        {
            ProtectedApiToken = _protector.Protect(apiToken),
            AccountId = account?.Id,
            AccountName = string.IsNullOrEmpty(account?.Name) ? null : account.Name,
            TokenId = string.IsNullOrEmpty(token.Id) ? null : token.Id,
            ExpiresOn = token.ExpiresOn,
            VerifiedAt = now
        });

        _logger.LogInformation("Saved the Cloudflare API token {TokenId} for account {AccountId}",
            saved.TokenId, saved.AccountId ?? "(none)");
        return new CloudflareCredentialCheck(ToStatus(saved, true), await CheckPermissionsAsync(apiToken, saved.AccountId));
    }

    public async Task<CloudflareCredentialCheck> CheckAsync()
    {
        var saved = await _reader.GetAsync() ?? throw UserFacingException.Conflict(MissingMessage);
        var apiToken = Reveal(saved) ?? throw UserFacingException.Conflict(UnreadableMessage);

        var token = await VerifyAsync(apiToken, saved.AccountId);
        saved.VerifiedAt = _time.GetUtcNow().UtcDateTime;
        saved.ExpiresOn = token.ExpiresOn;
        await _writer.MarkVerifiedAsync(saved.VerifiedAt.Value, saved.ExpiresOn);

        return new CloudflareCredentialCheck(ToStatus(saved, true), await CheckPermissionsAsync(apiToken, saved.AccountId));
    }

    public async Task RemoveAsync()
    {
        _logger.LogWarning("Removing the saved Cloudflare API token");
        await _writer.DeleteAsync();
    }

    public async Task<CloudflareCredentials?> ResolveAsync()
    {
        var saved = await _reader.GetAsync();
        if (saved is null) return null;

        var apiToken = Reveal(saved);
        return apiToken is null ? null : new CloudflareCredentials(apiToken, saved.AccountId);
    }

    public async Task<CloudflareCredentials> RequireAsync(bool needsAccount = false)
    {
        var saved = await _reader.GetAsync() ?? throw UserFacingException.Conflict(MissingMessage);
        var apiToken = Reveal(saved) ?? throw UserFacingException.Conflict(UnreadableMessage);
        if (needsAccount && string.IsNullOrEmpty(saved.AccountId))
        {
            throw UserFacingException.Conflict(
                "The saved Cloudflare API token has no account permissions, which the tunnel and Access need. "
                + "Replace it on the Settings page with one that includes the Account permissions listed there.");
        }

        return new CloudflareCredentials(apiToken, saved.AccountId);
    }

    // ── helpers ──────────────────────────────────────────────────

    /// <summary>The token in the clear: decrypted, or as-is if it predates encryption (see CloudflareTokenStore).</summary>
    private string? Reveal(CloudflareCredential saved) =>
        CloudflareTokenProtector.IsProtected(saved.ProtectedApiToken)
            ? _protector.Unprotect(saved.ProtectedApiToken)
            : saved.ProtectedApiToken;

    private static CloudflareCredentialStatus ToStatus(CloudflareCredential c, bool readable) =>
        new(true, readable, c.AccountId, c.AccountName, c.TokenId, c.ExpiresOn, c.VerifiedAt, c.UpdatedAt);

    /// <summary>Cloudflare's verify call, with each way a token can be unusable turned into what to fix.</summary>
    private async Task<CloudflareTokenInfo> VerifyAsync(string apiToken, string? accountId)
    {
        CloudflareTokenInfo token;
        try
        {
            token = await _accounts.VerifyTokenAsync(apiToken, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloudflare did not verify the API token");
            throw new UserFacingException($"Cloudflare did not accept this token: {ex.Message}", ex, StatusCodes.Status400BadRequest);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        if (token.NotBefore is { } notBefore && notBefore > now)
        {
            throw new UserFacingException(
                $"This token can't be used until {notBefore:yyyy-MM-dd HH:mm} UTC. In Cloudflare, edit the token and "
                + "set its start date to today (or choose a preset expiry instead of a custom window), then save it again.");
        }

        if (token.ExpiresOn is { } expires && expires <= now
            || string.Equals(token.Status, "expired", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFacingException("This token has expired. Roll it or create a new one in Cloudflare.");
        }

        if (!string.Equals(token.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFacingException($"Cloudflare reports this token as {token.Status}. Enable it or create a new one.");
        }

        return token;
    }

    /// <summary>
    /// The account the token works in: the one given if the token can see it, else the only
    /// one it can see. None at all is allowed — a zone-only token can still manage DNS.
    /// </summary>
    private async Task<CloudflareAccount?> ResolveAccountAsync(string apiToken, string? accountId)
    {
        List<CloudflareAccount> visible;
        try
        {
            visible = await _accounts.ListAccountsAsync(apiToken);
        }
        catch (Exception ex)
        {
            // A token without account permissions can be refused here outright; that is not an error.
            _logger.LogInformation(ex, "Could not list accounts with the token; treating it as zone-only");
            visible = [];
        }

        if (accountId is not null)
        {
            var match = visible.FirstOrDefault(a => string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
            if (visible.Count == 0) return new CloudflareAccount { Id = accountId };

            throw new UserFacingException(
                $"This token can't see account {accountId}. It can see: {Describe(visible)}. "
                + "Check the Account ID, or the token's Account Resources.");
        }

        return visible.Count switch
        {
            0 => null,
            1 => visible[0],
            _ => throw new UserFacingException(
                $"This token can see several accounts ({Describe(visible)}). Enter the Account ID StageZero should use.")
        };
    }

    private static string Describe(IEnumerable<CloudflareAccount> accounts) =>
        string.Join(", ", accounts.Select(a => string.IsNullOrEmpty(a.Name) ? a.Id : $"{a.Name} ({a.Id})"));

    /// <summary>
    /// A saved tunnel lives in one account; a token for another (or for none) would strand it,
    /// so switching accounts means disconnecting the tunnel first.
    /// </summary>
    private async Task RequireSameTunnelAccountAsync(string? newAccountId)
    {
        var tunnel = await _tunnelConfig.GetAsync();
        if (tunnel is null || !tunnel.IsConfigured) return;

        var current = (await _reader.GetAsync())?.AccountId;
        if (current is null || string.Equals(current, newAccountId, StringComparison.OrdinalIgnoreCase)) return;

        throw UserFacingException.Conflict(newAccountId is null
            ? $"The tunnel {tunnel.TunnelName} is managed in account {current}, and this token has no account permissions. "
              + "Use a token that includes the Account permissions, or disconnect the tunnel on Tunnel Setup first."
            : $"The tunnel {tunnel.TunnelName} is managed in account {current}, but this token is for account {newAccountId}. "
              + "Use a token for the same account, or disconnect the tunnel on Tunnel Setup first.");
    }

    /// <summary>
    /// Read-only probes, one per permission. Cloudflare has no way to ask a token what it may
    /// do, and an Edit permission can't be proven without writing, so DNS Edit is reported from
    /// a successful read and confirmed for real on the first update.
    /// </summary>
    private async Task<List<CloudflarePermissionResult>> CheckPermissionsAsync(string apiToken, string? accountId)
    {
        var results = new List<CloudflarePermissionResult>();

        List<CloudflareZone> zones = [];
        try
        {
            zones = await _dns.GetZonesAsync(apiToken);
            results.Add(zones.Count > 0
                ? new(ZoneRead, true, $"{zones.Count} zone(s) visible: {string.Join(", ", zones.Select(z => z.Name).Take(5))}"
                                      + (zones.Count > 5 ? ", …" : string.Empty))
                : new(ZoneRead, false, "No zones are visible. Add Zone → Zone → Read and include your zones."));
        }
        catch (Exception ex)
        {
            results.Add(new(ZoneRead, false, ex.Message));
        }

        if (zones.Count == 0)
        {
            results.Add(new(DnsEdit, false, "Can't be checked without a visible zone."));
        }
        else
        {
            try
            {
                await _dns.GetDnsRecordsAsync(apiToken, zones[0].Id);
                results.Add(new(DnsEdit, true, $"Can read records in {zones[0].Name}; write access is confirmed on the first update."));
            }
            catch (Exception ex)
            {
                results.Add(new(DnsEdit, false, ex.Message));
            }
        }

        if (accountId is null)
        {
            const string noAccount = "The token has no account permissions, so it reaches no account.";
            results.Add(new(TunnelEdit, false, noAccount));
            results.Add(new(AccessAppsEdit, false, noAccount));
            results.Add(new(AccessServiceTokensEdit, false, noAccount));
        }
        else
        {
            try
            {
                var tunnels = await _tunnels.ListTunnelsAsync(apiToken, accountId);
                results.Add(new(TunnelEdit, true, $"{tunnels.Count} tunnel(s) on the account."));
            }
            catch (Exception ex)
            {
                results.Add(new(TunnelEdit, false, ex.Message));
            }

            try
            {
                var access = await _access.CheckPermissionsAsync(apiToken, accountId);
                results.Add(new(AccessAppsEdit, access.CanManageApplications,
                    access.CanManageApplications ? "Can reach Access applications." : "Cloudflare refused the Access applications list."));
                results.Add(new(AccessServiceTokensEdit, access.CanManageServiceTokens,
                    access.CanManageServiceTokens ? "Can reach Access service tokens." : "Cloudflare refused the service tokens list."));
            }
            catch (Exception ex)
            {
                results.Add(new(AccessAppsEdit, false, ex.Message));
                results.Add(new(AccessServiceTokensEdit, false, ex.Message));
            }
        }

        // In the table's order, so the page can line them up.
        return results.OrderBy(r => RequiredPermissions.ToList().IndexOf(r.Permission)).ToList();
    }
}
