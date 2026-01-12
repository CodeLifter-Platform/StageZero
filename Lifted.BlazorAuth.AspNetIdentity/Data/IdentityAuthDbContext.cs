using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Lifted.BlazorAuth.AspNetIdentity.Models;

namespace Lifted.BlazorAuth.AspNetIdentity.Data;

/// <summary>
/// Identity authentication database context containing ApplicationUser and Identity tables.
/// </summary>
public class IdentityAuthDbContext : IdentityDbContext<ApplicationUser>
{
    public IdentityAuthDbContext(DbContextOptions options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure ApplicationUser entity
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.RequiresPasswordChange).IsRequired();
        });

        // Customize Identity table names (optional - uncomment if you want custom names)
        // builder.Entity<ApplicationUser>().ToTable("Users");
        // builder.Entity<IdentityRole>().ToTable("Roles");
        // builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        // builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        // builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        // builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");
        // builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
    }
}

