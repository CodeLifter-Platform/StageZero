using StageZero.Services.Database;

namespace StageZero.Api;

/// <summary>
/// The Database page: where the data is, a connection test, and the switch between SQLite and
/// PostgreSQL. A connection string goes in (it carries a password) and never comes back out:
/// the status answers with a description that omits it.
/// </summary>
public static class DatabaseEndpoints
{
    public static void MapDatabaseEndpoints(this IEndpointRouteBuilder api)
    {
        var database = api.MapGroup("/database").RequireAuthorization(Session.OperatorPolicy).WithUserFacingErrors();

        database.MapGet("", (IDatabaseSwitchService service) => service.Status());

        database.MapPost("/test", (ConnectionInput input, IDatabaseSwitchService service, CancellationToken cancellation) =>
            service.TestAsync(input.ConnectionString, cancellation));

        database.MapPost("/switch", (SwitchInput input, IDatabaseSwitchService service, CancellationToken cancellation) =>
            service.SwitchToPostgresAsync(input.ConnectionString, input.CopyData, cancellation));

        database.MapPost("/revert", (IDatabaseSwitchService service) => service.RevertToSqlite());

        // "Use PostgreSQL": what is on the machine, and a new container when there is nothing.
        database.MapGet("/postgres/discover", (PostgresDiscovery discovery, CancellationToken cancellation) =>
            discovery.DiscoverAsync(cancellation));

        // Both passwords go in here once and never come back; the response names the host,
        // port, database and role so the page can build the connection string itself.
        database.MapPost("/postgres/create", (CreatePostgresRequest request, PostgresProvisioner provisioner, CancellationToken cancellation) =>
            provisioner.CreateAsync(request, cancellation));
    }

    public sealed record ConnectionInput(string ConnectionString);

    public sealed record SwitchInput(string ConnectionString, bool CopyData);
}
