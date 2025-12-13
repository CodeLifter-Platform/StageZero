using Microsoft.EntityFrameworkCore;
using StageZero.Models;

namespace StageZero.Data;

/// <summary>
/// Application database context for SQLite.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<IpCheck> IpChecks => Set<IpCheck>();
    public DbSet<DnsProvider> DnsProviders => Set<DnsProvider>();
    public DbSet<DnsRecord> DnsRecords => Set<DnsRecord>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

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

        // Configure IpCheck entity
        builder.Entity<IpCheck>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IpAddress).HasMaxLength(45).IsRequired();
            entity.Property(e => e.PreviousIpAddress).HasMaxLength(45);
            entity.HasIndex(e => e.CheckedAt);
        });

        // Configure DnsProvider entity
        builder.Entity<DnsProvider>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ProviderType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ApiToken).IsRequired();
            entity.Property(e => e.ZoneId).HasMaxLength(100);
        });

        // Configure DnsRecord entity
        builder.Entity<DnsRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RecordName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.RecordType).HasMaxLength(10).IsRequired();
            entity.Property(e => e.RecordId).HasMaxLength(100);
            entity.Property(e => e.LastIpAddress).HasMaxLength(45);
            entity.Property(e => e.Content).HasMaxLength(255);

            entity.HasOne(e => e.DnsProvider)
                .WithMany(p => p.DnsRecords)
                .HasForeignKey(e => e.DnsProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure AppSettings entity
        builder.Entity<AppSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Value).IsRequired();
            entity.HasIndex(e => e.Key).IsUnique();
        });
    }
}

