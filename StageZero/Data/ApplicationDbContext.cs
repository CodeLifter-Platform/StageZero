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
    public DbSet<AccessServiceToken> AccessServiceTokens => Set<AccessServiceToken>();

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

            // The access section lives in the same row rather than a side table: a route and
            // its Access settings are always read and written together, and one hostname
            // never has more than one of them.
            entity.OwnsOne(e => e.Access, access =>
            {
                access.Property(a => a.Mode)
                    .HasColumnName("AccessMode")
                    .HasMaxLength(20)
                    .IsRequired()
                    .HasConversion(
                        mode => mode.ToWireValue(),
                        value => AccessModes.Parse(value));

                access.Property(a => a.AllowedEmails).HasColumnName("AccessAllowedEmails");
                access.Property(a => a.AllowedEmailDomains).HasColumnName("AccessAllowedEmailDomains");
                access.Property(a => a.AllowedIdpIds).HasColumnName("AccessAllowedIdpIds");
                access.Property(a => a.SessionDuration).HasColumnName("AccessSessionDuration").HasMaxLength(50);
                access.Property(a => a.CreateServiceToken).HasColumnName("AccessCreateServiceToken");
                access.Property(a => a.ServiceTokenName).HasColumnName("AccessServiceTokenName").HasMaxLength(255);
                access.Property(a => a.ServiceTokenId).HasColumnName("AccessServiceTokenId").HasMaxLength(100);
                access.Property(a => a.ServiceTokenDuration).HasColumnName("AccessServiceTokenDuration").HasMaxLength(50);
                access.Property(a => a.ApplicationId).HasColumnName("AccessApplicationId").HasMaxLength(100);
                access.Property(a => a.IdentityPolicyId).HasColumnName("AccessIdentityPolicyId").HasMaxLength(100);
                access.Property(a => a.ServiceTokenPolicyId).HasColumnName("AccessServiceTokenPolicyId").HasMaxLength(100);
                access.Property(a => a.SyncedAt).HasColumnName("AccessSyncedAt");

                access.Ignore(a => a.AllowedEmailList);
                access.Ignore(a => a.AllowedEmailDomainList);
                access.Ignore(a => a.AllowedIdpIdList);
            });

            entity.Navigation(e => e.Access).IsRequired();
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

        // Configure AccessServiceToken entity
        builder.Entity<AccessServiceToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CloudflareTokenId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ClientId).HasMaxLength(255);
            entity.Property(e => e.Duration).HasMaxLength(50);
            entity.HasIndex(e => e.CloudflareTokenId).IsUnique();
        });
    }
}

