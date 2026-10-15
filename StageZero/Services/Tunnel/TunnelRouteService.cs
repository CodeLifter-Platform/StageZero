using Lifted.BlazorAuth.Basic.DataAdapters;
using StageZero.DataAdapters.Settings;
using StageZero.DataAdapters.TunnelRoutes;
using StageZero.Models;
using StageZero.Services.Access;

namespace StageZero.Services.Tunnel;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The Tunnel Routes screens: list, add, edit, enable/disable and delete hostnames, and read
/// their live Access status. Every write goes on to <see cref="ITunnelSyncService"/>, so
/// Cloudflare and the database never drift.
/// </summary>
public interface ITunnelRouteService
{
    Task<List<TunnelRoute>> GetRoutesAsync();
    Task<TunnelRoute?> GetRouteAsync(int id);

    /// <summary>The tunnel the routes publish through, or null until setup has run.</summary>
    Task<ResolvedTunnelConfig?> GetTunnelAsync();

    /// <summary>
    /// The access section a brand new hostname starts with: identity mode allowing the
    /// configured owner email. Opting out is a deliberate change from this default.
    /// </summary>
    Task<RouteAccessSettings> BuildDefaultAccessAsync();

    /// <summary>
    /// Creates (<paramref name="id"/> null) or updates a route from what the form submitted,
    /// and pushes it to Cloudflare. The result carries a newly minted service token when one
    /// was created: the only time its secret is available.
    /// </summary>
    Task<(TunnelRoute Route, RouteSyncResult Sync)> SaveRouteAsync(int? id, TunnelRouteInput input);

    /// <summary>Flips the enabled flag. Re-enabling returns a sync result, which can carry a new token.</summary>
    Task<(TunnelRoute Route, RouteSyncResult? Sync)> ToggleRouteAsync(int id);

    /// <summary>Deletes the route and what Cloudflare holds for it; says what was kept.</summary>
    Task<AccessTeardownResult> DeleteRouteAsync(int id);

    Task<AccessStatus> GetAccessStatusAsync(int id);

    /// <summary>Empty when the tunnel isn't set up or Cloudflare can't be reached: the picker is a convenience.</summary>
    Task<List<AccessIdentityProvider>> GetIdentityProvidersAsync();

    /// <summary>Empty when the tunnel isn't set up or Cloudflare can't be reached.</summary>
    Task<List<AccessServiceTokenInfo>> GetServiceTokensAsync();
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// What the route form may set: the hostname, the origin, and the desired Access
/// configuration. Cloudflare object IDs StageZero provisioned are never taken from a client.
/// </summary>
public sealed record TunnelRouteInput(
    string DomainName,
    string ForwardScheme,
    string ForwardHost,
    int ForwardPort,
    bool IsEnabled,
    string? Notes,
    RouteAccessInput Access);

public sealed record RouteAccessInput(
    AccessMode Mode,
    string? AllowedEmails,
    string? AllowedEmailDomains,
    string? AllowedIdpIds,
    string? SessionDuration,
    bool CreateServiceToken,
    string? ServiceTokenName,
    string? ServiceTokenId,
    string? ServiceTokenDuration);

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class TunnelRouteService : ITunnelRouteService
{
    /// <summary>Settings key overriding which email new hostnames are opened up to.</summary>
    public const string OwnerEmailSettingKey = "Access.OwnerEmail";

    private static readonly HashSet<string> Schemes = new(StringComparer.Ordinal) { "http", "https" };

    private readonly ILogger<TunnelRouteService> _logger;
    private readonly ITunnelRouteReader _routeReader;
    private readonly ITunnelRouteWriter _routeWriter;
    private readonly ITunnelSyncService _syncService;
    private readonly IAccessProvisioningService _accessProvisioning;
    private readonly ISettingsReader _settingsReader;
    private readonly IUserReader _userReader;

    public TunnelRouteService(
        ILogger<TunnelRouteService> logger,
        ITunnelRouteReader routeReader,
        ITunnelRouteWriter routeWriter,
        ITunnelSyncService syncService,
        IAccessProvisioningService accessProvisioning,
        ISettingsReader settingsReader,
        IUserReader userReader)
    {
        _logger = logger;
        _routeReader = routeReader;
        _routeWriter = routeWriter;
        _syncService = syncService;
        _accessProvisioning = accessProvisioning;
        _settingsReader = settingsReader;
        _userReader = userReader;
    }

    public Task<List<TunnelRoute>> GetRoutesAsync() => _routeReader.GetAllAsync();

    public Task<TunnelRoute?> GetRouteAsync(int id) => _routeReader.GetByIdAsync(id);

    public Task<ResolvedTunnelConfig?> GetTunnelAsync() => _syncService.GetResolvedConfigAsync();

    public async Task<RouteAccessSettings> BuildDefaultAccessAsync()
    {
        var settings = new RouteAccessSettings
        {
            Mode = AccessMode.Identity,
            SessionDuration = RouteAccessSettings.DefaultSessionDuration
        };

        var ownerEmail = await ResolveOwnerEmailAsync();
        if (!string.IsNullOrWhiteSpace(ownerEmail))
        {
            settings.AllowedEmails = ownerEmail;
        }

        return settings;
    }

    /// <summary>
    /// The email new hostnames are opened up to. An explicit Access.OwnerEmail setting wins;
    /// otherwise the first account created during setup is treated as the owner.
    /// </summary>
    private async Task<string?> ResolveOwnerEmailAsync()
    {
        var configured = await _settingsReader.GetValueAsync(OwnerEmailSettingKey);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().ToLowerInvariant();
        }

        var users = await _userReader.GetAllAsync();
        return users.OrderBy(u => u.Id).FirstOrDefault()?.Email.Trim().ToLowerInvariant();
    }

    public async Task<(TunnelRoute Route, RouteSyncResult Sync)> SaveRouteAsync(int? id, TunnelRouteInput input)
    {
        TunnelRoute route;
        if (id is { } existingId)
        {
            route = await _routeReader.GetByIdAsync(existingId)
                    ?? throw UserFacingException.NotFound("That route no longer exists.");
        }
        else
        {
            route = new TunnelRoute();
        }

        Apply(route, input);

        var duplicate = await _routeReader.GetByDomainAsync(route.DomainName);
        if (duplicate is not null && duplicate.Id != route.Id)
        {
            throw UserFacingException.Conflict($"A route for '{route.DomainName}' already exists.");
        }

        // Catch bad Access settings here rather than after the DNS record exists, so a
        // typo in an allowed email never costs a rollback.
        var accessErrors = route.Access.Validate();
        if (accessErrors.Count > 0)
        {
            throw new UserFacingException(string.Join(" ", accessErrors));
        }

        if (route.Id == 0)
        {
            _logger.LogInformation("Creating tunnel route {DomainName}", route.DomainName);
            await _routeWriter.InsertAsync(route);
        }
        else
        {
            _logger.LogInformation("Updating tunnel route {DomainName}", route.DomainName);
            await _routeWriter.UpdateAsync(route);
        }

        // Push to Cloudflare after the write so a failed sync leaves a saved route the user
        // can retry, rather than losing their input.
        try
        {
            var result = await _syncService.SyncRouteAsync(route);
            return (route, result);
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogError(ex, "Failed to sync tunnel route {DomainName}", route.DomainName);
            throw new UserFacingException($"Could not save the route: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Copies the form's fields onto the route, normalised and checked. Provisioned IDs on
    /// <see cref="RouteAccessSettings"/> are left as they are.
    /// </summary>
    public static void Apply(TunnelRoute route, TunnelRouteInput input)
    {
        var domain = input.DomainName?.Trim().ToLowerInvariant() ?? string.Empty;
        var scheme = input.ForwardScheme?.Trim().ToLowerInvariant() ?? string.Empty;
        var host = input.ForwardHost?.Trim() ?? string.Empty;

        if (domain.Length == 0) throw new UserFacingException("Hostname is required.");
        if (!Schemes.Contains(scheme)) throw new UserFacingException("Scheme must be http or https.");
        if (host.Length == 0) throw new UserFacingException("Forward host is required.");
        if (input.ForwardPort is < 1 or > 65535) throw new UserFacingException("Forward port must be between 1 and 65535.");
        if (input.Access is null) throw new UserFacingException("The Access section is required.");

        route.DomainName = domain;
        route.ForwardScheme = scheme;
        route.ForwardHost = host;
        route.ForwardPort = input.ForwardPort;
        route.IsEnabled = input.IsEnabled;
        route.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        route.UpdatedAt = DateTime.UtcNow;

        var access = route.Access;
        access.Mode = input.Access.Mode;
        access.AllowedEmails = Blank(input.Access.AllowedEmails);
        access.AllowedEmailDomains = Blank(input.Access.AllowedEmailDomains);
        access.AllowedIdpIds = Blank(input.Access.AllowedIdpIds);
        access.SessionDuration = Blank(input.Access.SessionDuration);
        access.CreateServiceToken = input.Access.CreateServiceToken;
        access.ServiceTokenName = Blank(input.Access.ServiceTokenName);
        access.ServiceTokenId = Blank(input.Access.ServiceTokenId);
        access.ServiceTokenDuration = Blank(input.Access.ServiceTokenDuration);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<(TunnelRoute Route, RouteSyncResult? Sync)> ToggleRouteAsync(int id)
    {
        var route = await _routeReader.GetByIdAsync(id)
                    ?? throw UserFacingException.NotFound("That route no longer exists.");
        try
        {
            route.IsEnabled = !route.IsEnabled;
            await _routeWriter.UpdateAsync(route);

            // Disabling drops the ingress rule but keeps the CNAME, so re-enabling does not
            // wait on DNS propagation. Re-enabling re-provisions Access, which can mint a
            // pending service token, so its result goes back to the caller.
            RouteSyncResult? result = null;
            if (route.IsEnabled)
            {
                result = await _syncService.SyncRouteAsync(route);
            }
            else
            {
                await _syncService.SyncAllRoutesAsync();
            }

            _logger.LogInformation("Tunnel route {DomainName} is now {Status}",
                route.DomainName, route.IsEnabled ? "enabled" : "disabled");
            return (route, result);
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogError(ex, "Failed to toggle tunnel route {RouteId}", id);
            throw new UserFacingException($"Could not toggle the route: {ex.Message}", ex);
        }
    }

    public async Task<AccessTeardownResult> DeleteRouteAsync(int id)
    {
        var route = await _routeReader.GetByIdAsync(id)
                    ?? throw UserFacingException.NotFound("That route no longer exists.");
        try
        {
            _logger.LogInformation("Deleting tunnel route {DomainName}", route.DomainName);
            await _routeWriter.DeleteAsync(route);
            return await _syncService.RemoveRouteAsync(route);
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogError(ex, "Failed to delete tunnel route {RouteId}", id);
            throw new UserFacingException($"Could not delete the route: {ex.Message}", ex);
        }
    }

    public async Task<AccessStatus> GetAccessStatusAsync(int id)
    {
        var route = await _routeReader.GetByIdAsync(id)
                    ?? throw UserFacingException.NotFound("That route no longer exists.");
        var config = await _syncService.GetResolvedConfigAsync()
                     ?? throw UserFacingException.Conflict(
                         "Cloudflare Tunnel is not configured. Complete Tunnel Setup first.");
        try
        {
            return await _accessProvisioning.GetStatusAsync(config, route);
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogError(ex, "Failed to read Access status for route {RouteId}", id);
            throw new UserFacingException($"Could not read the Access status: {ex.Message}", ex);
        }
    }

    public async Task<List<AccessIdentityProvider>> GetIdentityProvidersAsync()
    {
        try
        {
            var config = await _syncService.GetResolvedConfigAsync();
            return config is null ? new() : await _accessProvisioning.ListIdentityProvidersAsync(config);
        }
        catch (Exception ex)
        {
            // Leaving the picker empty means "every account IdP", which is exactly what
            // Cloudflare does when none is named.
            _logger.LogWarning(ex, "Could not list Cloudflare Access identity providers");
            return new();
        }
    }

    public async Task<List<AccessServiceTokenInfo>> GetServiceTokensAsync()
    {
        try
        {
            var config = await _syncService.GetResolvedConfigAsync();
            return config is null ? new() : await _accessProvisioning.ListServiceTokensAsync(config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list Cloudflare Access service tokens");
            return new();
        }
    }
}
