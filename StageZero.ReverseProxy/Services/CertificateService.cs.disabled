using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StageZero.ReverseProxy.Models;

namespace StageZero.ReverseProxy.Services
{
    public interface ICertificateService
    {
        Task<bool> RequestCertificateAsync(ProxyHost proxyHost);
        Task<X509Certificate2?> GetCertificateAsync(string domain);
        Task RenewExpiringCertificatesAsync();
    }

    public class CertificateService : ICertificateService
    {
        private readonly DbContext _dbContext;
        private readonly ILogger<CertificateService> _logger;
        private readonly IHostEnvironment _environment;
        private readonly string _certificatesPath;

        public CertificateService(
            DbContext dbContext,
            ILogger<CertificateService> logger,
            IHostEnvironment environment)
        {
            _dbContext = dbContext;
            _logger = logger;
            _environment = environment;
            _certificatesPath = Path.Combine(environment.ContentRootPath, "certificates");

            if (!Directory.Exists(_certificatesPath))
            {
                Directory.CreateDirectory(_certificatesPath);
            }
        }

        public async Task<bool> RequestCertificateAsync(ProxyHost proxyHost)
        {
            if (!proxyHost.UseLetsEncrypt || string.IsNullOrEmpty(proxyHost.LetsEncryptEmail))
            {
                _logger.LogWarning("Let's Encrypt not enabled for {Domain}", proxyHost.DomainName);
                return false;
            }

            try
            {
                _logger.LogInformation("Requesting certificate for {Domain}", proxyHost.DomainName);

                var acme = new AcmeContext(WellKnownServers.LetsEncryptV2);
                var account = await acme.NewAccount(proxyHost.LetsEncryptEmail, termsOfServiceAgreed: true);

                var order = await acme.NewOrder(new[] { proxyHost.DomainName });

                var authz = (await order.Authorizations()).First();
                var httpChallenge = await authz.Http();
                var keyAuthz = httpChallenge.KeyAuthz;

                _logger.LogInformation("Challenge token: {Token}", httpChallenge.Token);
                _logger.LogInformation("Key Authorization: {KeyAuthz}", keyAuthz);

                await httpChallenge.Validate();

                var maxAttempts = 10;
                var attempt = 0;
                while (attempt < maxAttempts)
                {
                    await Task.Delay(2000);
                    var orderStatus = await order.Resource();
                    if (orderStatus.Status == OrderStatus.Ready || orderStatus.Status == OrderStatus.Valid)
                    {
                        break;
                    }
                    attempt++;
                }

                var privateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);

                var cert = await order.Generate(new CsrInfo
                {
                    CountryName = "US",
                    State = "State",
                    Locality = "City",
                    Organization = "Organization",
                    OrganizationUnit = "IT",
                    CommonName = proxyHost.DomainName
                }, privateKey);

                var certPem = cert.ToPem();
                var keyPem = privateKey.ToPem();

                var certPath = Path.Combine(_certificatesPath, $"{proxyHost.DomainName}.crt");
                var keyPath = Path.Combine(_certificatesPath, $"{proxyHost.DomainName}.key");

                await File.WriteAllTextAsync(certPath, certPem);
                await File.WriteAllTextAsync(keyPath, keyPem);

                proxyHost.SslCertificatePath = certPath;
                proxyHost.SslCertificateKeyPath = keyPath;
                proxyHost.SslCertificateExpiry = DateTime.UtcNow.AddDays(90);
                proxyHost.UpdatedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Certificate successfully obtained for {Domain}", proxyHost.DomainName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to request certificate for {Domain}", proxyHost.DomainName);
                return false;
            }
        }

        public async Task<X509Certificate2?> GetCertificateAsync(string domain)
        {
            var proxyHost = await _dbContext.Set<ProxyHost>()
                .FirstOrDefaultAsync(p => p.DomainName == domain);

            if (proxyHost?.SslCertificatePath == null || !File.Exists(proxyHost.SslCertificatePath))
            {
                return null;
            }

            try
            {
                var certPem = await File.ReadAllTextAsync(proxyHost.SslCertificatePath);
                var keyPem = await File.ReadAllTextAsync(proxyHost.SslCertificateKeyPath!);

                return X509Certificate2.CreateFromPem(certPem, keyPem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load certificate for {Domain}", domain);
                return null;
            }
        }

        public async Task RenewExpiringCertificatesAsync()
        {
            var expiringDate = DateTime.UtcNow.AddDays(30);

            var expiringHosts = await _dbContext.Set<ProxyHost>()
                .Where(p => p.UseLetsEncrypt && p.SslCertificateExpiry < expiringDate)
                .ToListAsync();

            foreach (var host in expiringHosts)
            {
                _logger.LogInformation("Renewing certificate for {Domain}", host.DomainName);
                await RequestCertificateAsync(host);
            }
        }
    }
}
