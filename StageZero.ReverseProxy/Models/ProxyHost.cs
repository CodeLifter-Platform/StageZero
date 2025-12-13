using System;
using System.ComponentModel.DataAnnotations;

namespace StageZero.ReverseProxy.Models
{
    public class ProxyHost
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string DomainName { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string ForwardScheme { get; set; } = "http";

        [Required]
        [MaxLength(255)]
        public string ForwardHost { get; set; } = string.Empty;

        [Required]
        public int ForwardPort { get; set; }

        public bool CacheAssets { get; set; }
        public bool BlockCommonExploits { get; set; }
        public bool WebSocketsSupport { get; set; }

        public bool SslEnabled { get; set; }
        public bool SslForced { get; set; }
        public bool Http2Support { get; set; } = true;
        public bool HstsEnabled { get; set; }
        public int HstsMaxAge { get; set; } = 31536000;

        [MaxLength(255)]
        public string? SslCertificatePath { get; set; }

        [MaxLength(255)]
        public string? SslCertificateKeyPath { get; set; }

        public DateTime? SslCertificateExpiry { get; set; }

        public bool UseLetsEncrypt { get; set; }

        [MaxLength(255)]
        public string? LetsEncryptEmail { get; set; }

        public bool IsEnabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(500)]
        public string? Notes { get; set; }

        public string ForwardUrl => $"{ForwardScheme}://{ForwardHost}:{ForwardPort}";
    }
}
