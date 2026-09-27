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
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>The migration that matches the last pre-migrations schema.</summary>
    public const string BaselineMigration = "20260927204016_InitialCreate";

    public static async Task InitializeAsync(string databasePath, ILogger logger, CancellationToken cancellationToken = default)
    {
        await LegacyDatabaseAdopter.AdoptAsync(databasePath, logger, cancellationToken);

        await using var db = CreateContext(databasePath);
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("Applying {Count} database migration(s): {Migrations}",
                pending.Count, string.Join(", ", pending));
        }

        await db.Database.MigrateAsync(cancellationToken);
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
/// Converts a pre-migrations database into one at <see cref="DatabaseInitializer.BaselineMigration"/>.
/// Nothing is changed in place: the legacy file is only renamed once its copy is complete,
/// so a failure leaves it exactly as it was.
/// </summary>
internal static class LegacyDatabaseAdopter
{
    private const string HistoryTable = "__EFMigrationsHistory";

    public static async Task<string?> AdoptAsync(string databasePath, ILogger logger, CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            return null;
        }

        await using (var legacy = Open(databasePath))
        {
            await legacy.OpenAsync(cancellationToken);
            var tables = await GetTablesAsync(legacy, "main", cancellationToken);
            if (tables.Count == 0 || tables.Contains(HistoryTable))
            {
                return null;
            }

            // Fold the WAL into the main file so the copy and the backup are complete.
            await ExecuteAsync(legacy, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
        }

        logger.LogWarning(
            "Database {DatabasePath} predates migrations; adopting it at the baseline schema", databasePath);

        var adoptingPath = databasePath + ".adopting";
        DeleteDatabaseFiles(adoptingPath);

        try
        {
            var columnTypes = await CreateBaselineAsync(adoptingPath, cancellationToken);
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
    /// Builds the baseline schema and returns each column's CLR type, which decides what a
    /// required column the legacy database lacks gets filled with.
    /// </summary>
    private static async Task<Dictionary<(string Table, string Column), Type>> CreateBaselineAsync(
        string path, CancellationToken cancellationToken)
    {
        await using var fresh = DatabaseInitializer.CreateContext(path);
        await fresh.GetService<IMigrator>().MigrateAsync(DatabaseInitializer.BaselineMigration, cancellationToken);

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
            foreach (var table in baselineTables.Where(t => t != HistoryTable))
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

            foreach (var dropped in legacyTables.Except(baselineTables))
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
