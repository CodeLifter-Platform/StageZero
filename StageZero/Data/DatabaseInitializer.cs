using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace StageZero.Data;

// ═══════════════════════════════════════════════════════════════
// DATABASE INITIALIZER
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Brings the database to the latest EF Core migration at startup.
/// <para>
/// Before migrations, the schema came from <c>EnsureCreated</c> plus hand-written
/// <c>ALTER TABLE</c> patches, so installs in the wild carry several different shapes of it.
/// Such a database is <b>adopted</b> once: a fresh database is built from the baseline
/// migration, the data is copied across column by column, and the original is kept beside
/// it as a <c>.bak</c>. After that every install has exactly the schema the migrations
/// describe, and each later change is an ordinary migration.
/// </para>
/// <para>
/// The migrations were regenerated once (September 2026), so an install migrated by the
/// earlier set has a history naming none of today's migrations. It is adopted the same way,
/// but at the <b>latest</b> migration: its schema and data already went through every change
/// the current migrations describe, and replaying them would undo some (a renamed column
/// would come back empty).
/// </para>
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>The migration that matches the last pre-migrations schema.</summary>
    public const string BaselineMigration = "20260927204016_InitialCreate";

    /// <summary>The migration that makes Users.Email compare without case (NOCASE / citext).</summary>
    public const string CaseInsensitiveEmailMigration = "CaseInsensitiveEmail";

    /// <summary>
    /// Brings whichever database <paramref name="settings"/> names to its latest migration.
    /// Only a SQLite file can be a legacy one; a PostgreSQL database has only ever been built
    /// by its own migrations.
    /// </summary>
    public static async Task InitializeAsync(DatabaseSettings settings, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (settings.SqlitePath is { } path)
        {
            await InitializeAsync(path, logger, cancellationToken);
            return;
        }

        await using var db = settings.CreateContext();
        await MigrateAsync(db, logger, cancellationToken);
    }

    public static async Task InitializeAsync(string databasePath, ILogger logger, CancellationToken cancellationToken = default)
    {
        await LegacyDatabaseAdopter.AdoptAsync(databasePath, logger, cancellationToken);

        await using var db = CreateContext(databasePath);
        await MigrateAsync(db, logger, cancellationToken);
    }

    private static async Task MigrateAsync(ApplicationDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("Applying {Count} database migration(s): {Migrations}",
                pending.Count, string.Join(", ", pending));
        }

        if (pending.Any(m => m.EndsWith("_" + CaseInsensitiveEmailMigration, StringComparison.Ordinal))
            && !pending.Any(m => m.EndsWith("_InitialCreate", StringComparison.Ordinal)))
        {
            await RefuseCaseDuplicateEmailsAsync(db, cancellationToken);
        }

        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Two accounts whose addresses differ only in case can't both survive the move to a
    /// case-insensitive unique index, and picking one would be deleting the other's account.
    /// So startup stops and names them; the operator removes the extra one by hand.
    /// </summary>
    private static async Task RefuseCaseDuplicateEmailsAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var duplicates = await db.Database
            .SqlQueryRaw<string>("""SELECT lower("Email") AS "Value" FROM "Users" GROUP BY lower("Email") HAVING COUNT(*) > 1""")
            .ToListAsync(cancellationToken);
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "Email addresses now ignore case, but these have more than one account in different cases: "
                + string.Join(", ", duplicates)
                + ". Delete the extra account(s) from the Users table, then start StageZero again.");
        }
    }

    /// <summary>A context on the given file, unpooled so no handle outlives it.</summary>
    public static ApplicationDbContext CreateContext(string databasePath) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options);
}

// ═══════════════════════════════════════════════════════════════
// LEGACY ADOPTION
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Converts a pre-migrations database into one at <see cref="DatabaseInitializer.BaselineMigration"/>,
/// or one migrated by the retired migration set into one at the latest migration.
/// Nothing is changed in place: the legacy file is only renamed once its copy is complete,
/// so a failure leaves it exactly as it was.
/// </summary>
internal static class LegacyDatabaseAdopter
{
    private const string HistoryTable = "__EFMigrationsHistory";
    private const string LockTable = "__EFMigrationsLock";

    public static async Task<string?> AdoptAsync(string databasePath, ILogger logger, CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            return null;
        }

        string? targetMigration = DatabaseInitializer.BaselineMigration;

        await using (var legacy = Open(databasePath))
        {
            await legacy.OpenAsync(cancellationToken);
            var tables = await GetTablesAsync(legacy, "main", cancellationToken);
            if (tables.Count == 0)
            {
                return null;
            }

            if (tables.Contains(HistoryTable))
            {
                if (await HasCurrentHistoryAsync(legacy, cancellationToken))
                {
                    return null;
                }

                targetMigration = null; // the retired set: already at today's schema
            }

            // Fold the WAL into the main file so the copy and the backup are complete.
            await ExecuteAsync(legacy, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
        }

        if (targetMigration is null)
        {
            logger.LogWarning(
                "Database {DatabasePath} was migrated by a retired migration set; adopting it at the latest schema",
                databasePath);
        }
        else
        {
            logger.LogWarning(
                "Database {DatabasePath} predates migrations; adopting it at the baseline schema", databasePath);
        }

        var adoptingPath = databasePath + ".adopting";
        DeleteDatabaseFiles(adoptingPath);

        try
        {
            var columnTypes = await CreateBaselineAsync(adoptingPath, targetMigration, cancellationToken);
            await CopyDataAsync(adoptingPath, databasePath, columnTypes, logger, cancellationToken);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            DeleteDatabaseFiles(adoptingPath);
            throw;
        }

        SqliteConnection.ClearAllPools();
        var backupPath = $"{databasePath}.pre-migrations-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
        MoveDatabaseFiles(databasePath, backupPath);
        MoveDatabaseFiles(adoptingPath, databasePath);

        logger.LogWarning("Adopted legacy database; the original is kept at {BackupPath}", backupPath);
        return backupPath;
    }

    /// <summary>
    /// Builds the schema at <paramref name="targetMigration"/> (null: the latest) and returns
    /// each column's CLR type, which decides what a required column the legacy database lacks
    /// gets filled with.
    /// </summary>
    private static async Task<Dictionary<(string Table, string Column), Type>> CreateBaselineAsync(
        string path, string? targetMigration, CancellationToken cancellationToken)
    {
        await using var fresh = DatabaseInitializer.CreateContext(path);
        await fresh.GetService<IMigrator>().MigrateAsync(targetMigration, cancellationToken);

        var columnTypes = new Dictionary<(string, string), Type>();
        foreach (var entity in fresh.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null)
            {
                continue;
            }

            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName();
                columnTypes[(table, column)] = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            }
        }

        return columnTypes;
    }

    private static async Task CopyDataAsync(
        string adoptingPath,
        string legacyPath,
        Dictionary<(string Table, string Column), Type> columnTypes,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var connection = Open(adoptingPath);
        await connection.OpenAsync(cancellationToken);

        // Rows arrive table by table, so a child can land before its parent.
        await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF;", cancellationToken);
        await ExecuteAsync(connection, $"ATTACH DATABASE {SqlString(legacyPath)} AS legacy;", cancellationToken);

        var legacyTables = await GetTablesAsync(connection, "legacy", cancellationToken);
        var baselineTables = await GetTablesAsync(connection, "main", cancellationToken);

        await using (var transaction = connection.BeginTransaction())
        {
            foreach (var table in baselineTables.Where(t => t is not (HistoryTable or LockTable)))
            {
                if (!legacyTables.Contains(table))
                {
                    continue;
                }

                var target = await GetColumnsAsync(connection, "main", table, cancellationToken);
                var source = (await GetColumnsAsync(connection, "legacy", table, cancellationToken))
                    .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

                var names = new List<string>();
                var values = new List<string>();
                foreach (var column in target)
                {
                    var expression = SourceExpression(table, column, source, columnTypes);
                    if (expression is null)
                    {
                        continue; // nullable or defaulted, and absent from the legacy table
                    }

                    names.Add(Identifier(column.Name));
                    values.Add(expression);
                }

                var sql = $"INSERT INTO main.{Identifier(table)} ({string.Join(", ", names)}) " +
                          $"SELECT {string.Join(", ", values)} FROM legacy.{Identifier(table)};";
                var copied = await ExecuteAsync(connection, sql, cancellationToken, transaction);
                logger.LogInformation("Adopted {Rows} row(s) of {Table}", copied, table);
            }

            foreach (var dropped in legacyTables.Except(baselineTables).Where(t => t != LockTable))
            {
                logger.LogInformation("Not carried forward: retired table {Table}", dropped);
            }

            transaction.Commit();
        }

        await ExecuteAsync(connection, "DETACH DATABASE legacy;", cancellationToken);
        await ExecuteAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
    }

    /// <summary>
    /// The SELECT expression that fills one baseline column from the legacy row, or null to
    /// leave it to its default.
    /// </summary>
    private static string? SourceExpression(
        string table,
        ColumnInfo column,
        Dictionary<string, ColumnInfo> source,
        Dictionary<(string Table, string Column), Type> columnTypes)
    {
        // Early auth schemas keyed users on Username, with Email optional.
        if (table == "Users" && column.Name == "Email" && source.ContainsKey("Username"))
        {
            return source.ContainsKey("Email")
                ? "COALESCE(NULLIF(\"Email\", ''), \"Username\")"
                : "\"Username\"";
        }

        // Accounts from before the forced-password-change column set a new password.
        if (table == "Users" && column.Name == "RequiresPasswordChange" && !source.ContainsKey(column.Name))
        {
            return "1";
        }

        var required = column.NotNull && column.DefaultValue is null && !column.IsPrimaryKey;
        if (source.ContainsKey(column.Name))
        {
            var fallback = required && !source[column.Name].NotNull
                ? FallbackValue(table, column, columnTypes)
                : null;
            return fallback is null
                ? Identifier(column.Name)
                : $"COALESCE({Identifier(column.Name)}, {fallback})";
        }

        return required ? FallbackValue(table, column, columnTypes) : null;
    }

    private static string FallbackValue(
        string table, ColumnInfo column, Dictionary<(string Table, string Column), Type> columnTypes)
    {
        var type = columnTypes.GetValueOrDefault((table, column.Name));
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return "strftime('%Y-%m-%d %H:%M:%f', 'now')";
        }

        return column.Type.ToUpperInvariant() switch
        {
            "INTEGER" or "REAL" or "NUMERIC" => "0",
            _ => "''"
        };
    }

    /// <summary>Whether the history names any migration this build knows.</summary>
    private static async Task<bool> HasCurrentHistoryAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var db = DatabaseInitializer.CreateContext(":memory:");
        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MigrationId FROM {Identifier(HistoryTable)};";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var empty = true;
        while (await reader.ReadAsync(cancellationToken))
        {
            empty = false;
            if (known.Contains(reader.GetString(0)))
            {
                return true;
            }
        }

        // An empty history is a migration that never finished its first step; let it retry.
        return empty;
    }

    // ───────────────────────────────────────────────────────────
    // SQLITE HELPERS
    // ───────────────────────────────────────────────────────────

    private sealed record ColumnInfo(string Name, string Type, bool NotNull, string? DefaultValue, bool IsPrimaryKey);

    private static SqliteConnection Open(string path) =>
        new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());

    private static async Task<HashSet<string>> GetTablesAsync(
        SqliteConnection connection, string schema, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT name FROM {schema}.sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";

        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<List<ColumnInfo>> GetColumnsAsync(
        SqliteConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {schema}.table_info({Identifier(table)});";

        var columns = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnInfo(
                Name: reader.GetString(1),
                Type: reader.GetString(2),
                NotNull: reader.GetInt64(3) != 0,
                DefaultValue: reader.IsDBNull(4) ? null : reader.GetString(4),
                IsPrimaryKey: reader.GetInt64(5) != 0));
        }

        return columns;
    }

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection, string sql, CancellationToken cancellationToken, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Identifier(string name) => $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string SqlString(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static readonly string[] SidecarSuffixes = ["", "-wal", "-shm", "-journal"];

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var suffix in SidecarSuffixes)
        {
            File.Delete(path + suffix);
        }
    }

    private static void MoveDatabaseFiles(string from, string to)
    {
        foreach (var suffix in SidecarSuffixes)
        {
            if (File.Exists(from + suffix))
            {
                File.Move(from + suffix, to + suffix);
            }
        }
    }
}
