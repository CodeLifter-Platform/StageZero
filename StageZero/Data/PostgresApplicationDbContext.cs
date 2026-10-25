using Lifted.BlazorAuth.Basic.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace StageZero.Data;

/// <summary>
/// The same model on PostgreSQL. It exists as its own type only so EF keeps PostgreSQL's
/// migrations (<c>Data/Migrations/Postgres/</c>) apart from SQLite's: EF picks the migrations
/// whose <c>[DbContext]</c> names the exact context type it is running. The app still asks for
/// <see cref="ApplicationDbContext"/> everywhere (<see cref="DatabaseSettings.AddDbContextFactory"/>).
/// </summary>
public sealed class PostgresApplicationDbContext : ApplicationDbContext
{
    public PostgresApplicationDbContext(DbContextOptions<PostgresApplicationDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Email in any case is one address: citext in place of SQLite's NOCASE collation, so the
    /// unique index, sign-in and every comparison ignore case while the address keeps the case
    /// it was typed in.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("citext");
        builder.Entity<User>().Property(u => u.Email).UseCollation(null).HasColumnType("citext");
    }

    /// <summary>
    /// Every time is UTC, but not every DateTime says so: SQLite hands them back unmarked, and
    /// timestamptz refuses an unmarked one. Unmarked is read as UTC, the way it was written.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

/// <summary>
/// Lets <c>dotnet ef migrations add &lt;Name&gt; --context PostgresApplicationDbContext
/// --output-dir Data/Migrations/Postgres</c> build the context without a server: migrations are
/// generated from the model alone, and this connection is never opened.
/// </summary>
public sealed class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgresApplicationDbContext>
{
    public PostgresApplicationDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=stagezero_design_time")
            .Options);
}
