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
