using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StageZero.Data;

namespace StageZero.Services.Cli;

/// <summary>
/// <c>StageZero copy-to-postgres</c>: moves an install from its SQLite file to PostgreSQL.
/// Reads the SQLite database in the data directory (adopting and migrating it first, as
/// startup would), and copies every row, ids included, into the empty PostgreSQL database
/// named by <c>STAGEZERO_DB_CONNECTION</c>, whatever <c>STAGEZERO_DB_PROVIDER</c> says: the
/// usual move is to set both for the app, run this once, and restart. The SQLite file is only
/// read, so going back is just unsetting <c>STAGEZERO_DB_PROVIDER</c>.
/// <para>
/// The stored Cloudflare token moves still encrypted. The keys that decrypt them stay in the
/// data directory, which the app keeps needing on PostgreSQL too.
/// </para>
/// </summary>
public static class CopyToPostgresCommand
{
    public const string Name = "copy-to-postgres";

    /// <summary>Parents before children, so every foreign key finds its row.</summary>
    private static readonly Func<ApplicationDbContext, ApplicationDbContext, CancellationToken, Task<int>>[] Tables =
    [
        Copy<Lifted.BlazorAuth.Basic.Models.User>,
        Copy<Models.AppSettings>,
        Copy<Models.DnsProvider>,
        Copy<Models.DnsRecord>,
        Copy<Models.IpCheck>,
        Copy<Models.TunnelConfig>,
        Copy<Models.TunnelRoute>,
        Copy<Models.AccessServiceToken>,
        Copy<Models.CloudflareCredential>,
    ];

    /// <summary>The entity types <see cref="Tables"/> copies; a table missing here is a bug.</summary>
    public static readonly Type[] CopiedTypes =
    [
        typeof(Lifted.BlazorAuth.Basic.Models.User),
        typeof(Models.AppSettings),
        typeof(Models.DnsProvider),
        typeof(Models.DnsRecord),
        typeof(Models.IpCheck),
        typeof(Models.TunnelConfig),
        typeof(Models.TunnelRoute),
        typeof(Models.AccessServiceToken),
        typeof(Models.CloudflareCredential),
    ];

    public static async Task<int> RunAsync(
        string sqlitePath, string? targetConnection, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetConnection))
        {
            await output.WriteLineAsync(
                $"Usage: {DatabaseSettings.ConnectionVariable}=<PostgreSQL connection string> StageZero {Name}");
            return 2;
        }

        if (!File.Exists(sqlitePath))
        {
            await output.WriteLineAsync($"No SQLite database at {sqlitePath}; nothing to copy.");
            return 1;
        }

        var source = DatabaseSettings.Sqlite(sqlitePath);
        var target = DatabaseSettings.Postgres(targetConnection);
        await output.WriteLineAsync($"From: {source.Describe()}");
        await output.WriteLineAsync($"To:   {target.Describe()}");

        await DatabaseInitializer.InitializeAsync(source, NullLogger.Instance, cancellationToken);
        await DatabaseInitializer.InitializeAsync(target, NullLogger.Instance, cancellationToken);

        await using var from = source.CreateContext();
        await using var to = target.CreateContext();

        var unlisted = to.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && !CopiedTypes.Contains(e.ClrType))
            .Select(e => e.ClrType.Name)
            .ToList();
        if (unlisted.Count > 0)
        {
            throw new InvalidOperationException($"{Name} does not know how to copy: {string.Join(", ", unlisted)}");
        }

        foreach (var table in to.Model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            if (await HasRowsAsync(to, table.GetTableName()!, cancellationToken))
            {
                await output.WriteLineAsync(
                    $"The PostgreSQL database already has data (in {table.GetTableName()}). " +
                    "Copy into an empty database, so nothing there is overwritten.");
                return 1;
            }
        }

        await using (var transaction = await to.Database.BeginTransactionAsync(cancellationToken))
        {
            for (var i = 0; i < Tables.Length; i++)
            {
                var copied = await Tables[i](from, to, cancellationToken);
                await output.WriteLineAsync($"  {CopiedTypes[i].Name}: {copied} row(s)");
            }

            // Explicit ids leave each identity sequence at 1; the next insert would collide.
            foreach (var table in to.Model.GetEntityTypes().Where(e => !e.IsOwned()))
            {
                // Identifiers from the model, never from input; they can't be parameters.
                var name = table.GetTableName()!.Replace("\"", "\"\"", StringComparison.Ordinal);
                var resetSequence =
                    $"SELECT setval(pg_get_serial_sequence('\"{name}\"', 'Id'), COALESCE(MAX(\"Id\"), 0) + 1, false) FROM \"{name}\";";
                await to.Database.ExecuteSqlRawAsync(resetSequence, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await output.WriteLineAsync(
            $"Done. Set {DatabaseSettings.ProviderVariable}=postgres and {DatabaseSettings.ConnectionVariable} for the app, " +
            "keep the data directory mounted (it holds the keys), and restart.");
        return 0;
    }

    private static async Task<int> Copy<T>(ApplicationDbContext from, ApplicationDbContext to, CancellationToken cancellationToken)
        where T : class
    {
        var rows = await from.Set<T>().AsNoTracking().ToListAsync(cancellationToken);
        // SQLite hands times back unmarked; PostgresApplicationDbContext writes them as UTC.
        to.Set<T>().AddRange(rows);
        await to.SaveChangesAsync(cancellationToken);
        to.ChangeTracker.Clear();
        return rows.Count;
    }

    private static async Task<bool> HasRowsAsync(ApplicationDbContext db, string table, CancellationToken cancellationToken)
    {
        var name = table.Replace("\"", "\"\"", StringComparison.Ordinal);
        var exists = $"SELECT EXISTS (SELECT 1 FROM \"{name}\") AS \"Value\"";
        return await db.Database.SqlQueryRaw<bool>(exists).SingleAsync(cancellationToken);
    }
}
