using Microsoft.EntityFrameworkCore;
using StageZero.Models;
using StageZero.ReverseProxy.Models;

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
    public DbSet<ProxyHost> ProxyHosts => Set<ProxyHost>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure User entity
        builder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.HasIndex(e => e.Username).IsUnique();
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

        // Configure ProxyHost entity
        builder.Entity<ProxyHost>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DomainName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ForwardScheme).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ForwardHost).HasMaxLength(255).IsRequired();
            entity.Property(e => e.SslCertificatePath).HasMaxLength(255);
            entity.Property(e => e.SslCertificateKeyPath).HasMaxLength(255);
            entity.Property(e => e.LetsEncryptEmail).HasMaxLength(255);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.HasIndex(e => e.DomainName).IsUnique();
            entity.HasIndex(e => e.IsEnabled);
        });
    }
}

