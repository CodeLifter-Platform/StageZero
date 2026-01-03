using Microsoft.EntityFrameworkCore;
using Lifted.BlazorAuth.Basic.Models;

namespace Lifted.BlazorAuth.Basic.Data;

/// <summary>
/// Basic authentication database context containing User entity.
/// </summary>
public class BasicAuthDbContext : DbContext
{
    public BasicAuthDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure User entity
        builder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
        });
    }
}

