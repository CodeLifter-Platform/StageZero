using Microsoft.EntityFrameworkCore;
using StageZero.Models;
using Lifted.BlazorAuth.Basic.Data;

namespace StageZero.Data;

/// <summary>
/// Application database context for SQLite.
/// </summary>
public class ApplicationDbContext : BasicAuthDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<IpCheck> IpChecks => Set<IpCheck>();
    public DbSet<DnsProvider> DnsProviders => Set<DnsProvider>();
    public DbSet<DnsRecord> DnsRecords => Set<DnsRecord>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<TunnelRoute> TunnelRoutes => Set<TunnelRoute>();
    public DbSet<TunnelConfig> TunnelConfigs => Set<TunnelConfig>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

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

        // Configure TunnelRoute entity
        builder.Entity<TunnelRoute>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DomainName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ForwardScheme).HasMaxLength(10).IsRequired();
            entity.Property(e => e.ForwardHost).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.HasIndex(e => e.DomainName).IsUnique();
            entity.HasIndex(e => e.IsEnabled);
        });

        // Configure TunnelConfig entity (single row)
        builder.Entity<TunnelConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CloudflareAccountId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CloudflareZoneId).HasMaxLength(100);
            entity.Property(e => e.CloudflareZoneName).HasMaxLength(255);
            entity.Property(e => e.ProtectedApiToken).IsRequired();
            entity.Property(e => e.TunnelId).HasMaxLength(100);
            entity.Property(e => e.TunnelName).HasMaxLength(255);
            entity.Ignore(e => e.IsConfigured);
        });
    }
}

