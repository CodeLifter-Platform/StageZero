using Lifted.BlazorAuth.Basic.DataAdapters;
using StageZero.Services.Database;

namespace StageZero.Api;

/// <summary>
/// Choosing the database in the web app: the Settings page's Database card (signed in), and
/// the same three calls on the first-run page, open only while the install has no user yet.
/// A PostgreSQL password goes in on a POST and never comes back out.
/// </summary>
public static class DatabaseEndpoints
{
    public static void MapDatabaseEndpoints(this IEndpointRouteBuilder api)
    {
        var settings = api.MapGroup("/settings/database").RequireAuthorization(Session.OperatorPolicy).WithUserFacingErrors();
        Map(settings);

        // Signed in only, never on the first-run page: finding servers is harmless, but
        // creating a container means driving the Docker engine, which is root on the host.
        settings.MapGet("/postgres/discover", (PostgresDiscovery discovery, CancellationToken cancellationToken) =>
            discovery.DiscoverAsync(cancellationToken));

        // Both passwords go in here once and never come back; the response names the host,
        // port, database and role so the form can be filled in.
        settings.MapPost("/postgres/create", (CreatePostgresRequest request, PostgresProvisioner provisioner, CancellationToken cancellationToken) =>
            provisioner.CreateAsync(request, cancellationToken));

        // First run: whoever reaches a fresh install first creates its admin, and may first
        // point it at PostgreSQL. Closed for good once any user exists.
        var setup = api.MapGroup("/setup/database").WithUserFacingErrors()
            .AddEndpointFilter(async (context, next) =>
            {
                var users = context.HttpContext.RequestServices.GetRequiredService<IUserReader>();
                return (await users.GetAllAsync()).Count > 0
                    ? Results.Problem(title: "Setup has already been completed.", statusCode: StatusCodes.Status409Conflict)
                    : await next(context);
            });
        Map(setup);
    }

    private static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", (IDatabaseSwitchService databases) => databases.GetStatus());

        group.MapPost("/test", (PostgresTarget target, IDatabaseSwitchService databases, CancellationToken cancellationToken) =>
            databases.TestAsync(target, cancellationToken));

        group.MapPost("/switch", (PostgresTarget target, IDatabaseSwitchService databases, CancellationToken cancellationToken) =>
            databases.SwitchToPostgresAsync(target, cancellationToken));
    }
}
