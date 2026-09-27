using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StageZero.Data;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without running Program.cs, which would resolve
/// the real data directory, start logging there, and load <c>.env</c>. Migrations are
/// generated from the model alone; this database file is never opened.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new ApplicationDbContext(options);
    }
}
