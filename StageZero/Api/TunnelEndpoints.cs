using StageZero.Models;
using StageZero.Services.Access;
using StageZero.Services.Tunnel;

namespace StageZero.Api;

/// <summary>
/// Tunnel setup and tunnel routes. Two secrets cross this boundary outward, both on purpose:
/// the connector token (the operator installs it on the host) and a freshly minted Access
/// service token's client secret, which appears in exactly one response and is stored nowhere.
/// </summary>
public static class TunnelEndpoints
{
    public static void MapTunnelEndpoints(this IEndpointRouteBuilder api)
    {
        var tunnel = api.MapGroup("/tunnel").RequireAuthorization(Session.OperatorPolicy).WithUserFacingErrors();

        // ── setup ──
        tunnel.MapGet("/config", async (ITunnelSetupService setup) =>
            new ConfigResponse(ConfigView.From(await setup.GetSavedConfigAsync())));

        tunnel.MapPost("/account", async (AccountRequest request, ITunnelSetupService setup) =>
        {
            var (zones, tunnels) = await setup.LoadAccountAsync(request.ApiToken ?? string.Empty, request.AccountId ?? string.Empty);
            return new AccountResponse(zones, tunnels.Select(TunnelView.From).ToList());
        });

        tunnel.MapPost("/create", async (CreateRequest request, ITunnelSetupService setup) =>
            SetupView.From(await setup.CreateTunnelAsync(
                request.ApiToken ?? string.Empty, request.AccountId ?? string.Empty,
                request.ZoneId ?? string.Empty, request.TunnelName ?? string.Empty)));

        tunnel.MapPost("/adopt", async (AdoptRequest request, ITunnelSetupService setup) =>
            SetupView.From(await setup.AdoptTunnelAsync(
                request.ApiToken ?? string.Empty, request.AccountId ?? string.Empty,
                request.ZoneId ?? string.Empty, request.TunnelId ?? string.Empty)));

        tunnel.MapPost("/connector-token", async (ITunnelSetupService setup) =>
            new ConnectorTokenResponse(await setup.GetConnectorTokenAsync()));

        tunnel.MapDelete("/config", async (ITunnelSetupService setup) =>
        {
            await setup.ClearConfigurationAsync();
            return Results.NoContent();
        });

        // ── routes ──
        var routes = tunnel.MapGroup("/routes");

        routes.MapGet("/", async (ITunnelRouteService service) =>
        {
            var config = await service.GetTunnelAsync();
            var all = await service.GetRoutesAsync();
            return new RoutesResponse(config is not null, config?.TunnelName, config?.ZoneName,
                all.Select(RouteView.From).ToList());
        });

        routes.MapGet("/defaults", async (ITunnelRouteService service) =>
            AccessView.From(await service.BuildDefaultAccessAsync()));

        routes.MapGet("/{id:int}", async (int id, ITunnelRouteService service) =>
            await service.GetRouteAsync(id) is { } route
                ? Results.Ok(RouteView.From(route))
                : Results.Problem(title: "That route no longer exists.", statusCode: StatusCodes.Status404NotFound));

        routes.MapPost("/", async (TunnelRouteInput input, ITunnelRouteService service) =>
        {
            var (route, sync) = await service.SaveRouteAsync(null, input);
            return SaveResponse.From(route, sync);
        });

        routes.MapPut("/{id:int}", async (int id, TunnelRouteInput input, ITunnelRouteService service) =>
        {
            var (route, sync) = await service.SaveRouteAsync(id, input);
            return SaveResponse.From(route, sync);
        });

        routes.MapPost("/{id:int}/toggle", async (int id, ITunnelRouteService service) =>
        {
            var (route, sync) = await service.ToggleRouteAsync(id);
            return SaveResponse.From(route, sync);
        });

        routes.MapDelete("/{id:int}", async (int id, ITunnelRouteService service) =>
        {
            var teardown = await service.DeleteRouteAsync(id);
            return new DeleteResponse(teardown.ServiceTokenRetainedReason, teardown.Warnings);
        });

        routes.MapGet("/{id:int}/access-status", (int id, ITunnelRouteService service) =>
            service.GetAccessStatusAsync(id));

        // ── Access pickers ──
        tunnel.MapGet("/access/identity-providers", (ITunnelRouteService service) => service.GetIdentityProvidersAsync());
        tunnel.MapGet("/access/service-tokens", (ITunnelRouteService service) => service.GetServiceTokensAsync());
    }

    // ── contracts ────────────────────────────────────────────────

    public sealed record AccountRequest(string? ApiToken, string? AccountId);

    public sealed record CreateRequest(string? ApiToken, string? AccountId, string? ZoneId, string? TunnelName);

    public sealed record AdoptRequest(string? ApiToken, string? AccountId, string? ZoneId, string? TunnelId);

    public sealed record ConfigResponse(ConfigView? Config);

    /// <summary>The saved configuration, without its API token.</summary>
    public sealed record ConfigView(string AccountId, string? ZoneId, string? ZoneName, string? TunnelId, string? TunnelName)
    {
        public static ConfigView? From(TunnelConfig? c) => c is null
            ? null
            : new(c.CloudflareAccountId, c.CloudflareZoneId, c.CloudflareZoneName, c.TunnelId, c.TunnelName);
    }

    public sealed record TunnelView(string Id, string Name, bool IsRemotelyManaged, string? Status)
    {
        public static TunnelView From(TunnelInfo t) => new(t.Id, t.Name, t.IsRemotelyManaged, t.Status);
    }

    public sealed record AccountResponse(IReadOnlyList<Services.Dns.CloudflareZone> Zones, IReadOnlyList<TunnelView> Tunnels);

    public sealed record SetupView(ConfigView Config, string ConnectorToken, bool WasLocallyManaged)
    {
        public static SetupView From(TunnelSetupResult r) => new(ConfigView.From(r.Config)!, r.ConnectorToken, r.WasLocallyManaged);
    }

    public sealed record ConnectorTokenResponse(string ConnectorToken);

    public sealed record RoutesResponse(bool IsConfigured, string? TunnelName, string? ZoneName, IReadOnlyList<RouteView> Routes);

    public sealed record RouteView(
        int Id, string DomainName, string ForwardScheme, string ForwardHost, int ForwardPort, string ForwardUrl,
        bool IsEnabled, string? Notes, AccessView Access, DateTime CreatedAt, DateTime UpdatedAt)
    {
        public static RouteView From(TunnelRoute r) => new(
            r.Id, r.DomainName, r.ForwardScheme, r.ForwardHost, r.ForwardPort, r.ForwardUrl,
            r.IsEnabled, r.Notes, AccessView.From(r.Access), r.CreatedAt, r.UpdatedAt);
    }

    /// <summary>The desired Access configuration plus when it last reached Cloudflare.</summary>
    public sealed record AccessView(
        AccessMode Mode, string ModeName, string? AllowedEmails, string? AllowedEmailDomains, string? AllowedIdpIds,
        string? SessionDuration, bool CreateServiceToken, string? ServiceTokenName, string? ServiceTokenId,
        string? ServiceTokenDuration, DateTime? SyncedAt)
    {
        public static AccessView From(RouteAccessSettings a) => new(
            a.Mode, a.Mode.ToDisplayName(), a.AllowedEmails, a.AllowedEmailDomains, a.AllowedIdpIds,
            a.SessionDuration, a.CreateServiceToken, a.ServiceTokenName, a.ServiceTokenId,
            a.ServiceTokenDuration, a.SyncedAt);
    }

    /// <summary>The one and only copy of a new service token's secret, for the operator to save.</summary>
    public sealed record NewServiceTokenView(string Name, string ClientId, string ClientSecret, DateTime? ExpiresAt);

    public sealed record SaveResponse(RouteView Route, NewServiceTokenView? NewServiceToken)
    {
        public static SaveResponse From(TunnelRoute route, RouteSyncResult? sync) => new(
            RouteView.From(route),
            sync?.Access?.NewServiceToken is { } t ? new(t.Name, t.ClientId, t.ClientSecret, t.ExpiresAt) : null);
    }

    public sealed record DeleteResponse(string? ServiceTokenRetainedReason, IReadOnlyList<string> Warnings);
}
