using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        private readonly IAcmeChallengeStore _challengeStore;
        private readonly Uri _acmeServer;
        private readonly string _certificatesPath;

        public CertificateService(
            DbContext dbContext,
            ILogger<CertificateService> logger,
            IHostEnvironment environment,
            IAcmeChallengeStore challengeStore,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _logger = logger;
            _environment = environment;
            _challengeStore = challengeStore;

            // Default to the Let's Encrypt staging directory unless production is
            // explicitly requested. Staging has far higher rate limits and issues
            // untrusted certs, which keeps experimentation from burning the
            // strict production rate limits. Set LetsEncrypt:UseProduction=true
            // (or LetsEncrypt__UseProduction=true) once issuance is verified.
            var useProduction = configuration.GetValue<bool>("LetsEncrypt:UseProduction");
            _acmeServer = useProduction
                ? WellKnownServers.LetsEncryptV2
                : WellKnownServers.LetsEncryptStagingV2;

            _certificatesPath = Path.Combine(environment.ContentRootPath, "certificates");

            if (!System.IO.Directory.Exists(_certificatesPath))
            {
                System.IO.Directory.CreateDirectory(_certificatesPath);
            }
        }

        public async Task<bool> RequestCertificateAsync(ProxyHost proxyHost)
        {
            if (!proxyHost.UseLetsEncrypt || string.IsNullOrEmpty(proxyHost.LetsEncryptEmail))
            {
                _logger.LogWarning("Let's Encrypt not enabled for {Domain}", proxyHost.DomainName);
                return false;
            }

            string? challengeToken = null;
            try
            {
                _logger.LogInformation("Requesting certificate for {Domain}", proxyHost.DomainName);

                var acme = new AcmeContext(_acmeServer);
                var account = await acme.NewAccount(proxyHost.LetsEncryptEmail, termsOfServiceAgreed: true);

                var order = await acme.NewOrder(new[] { proxyHost.DomainName });

                var authz = (await order.Authorizations()).First();
                var httpChallenge = await authz.Http();
                var keyAuthz = httpChallenge.KeyAuthz;

                // Publish the challenge response so /.well-known/acme-challenge/{token}
                // can serve it when Let's Encrypt validates the domain.
                challengeToken = httpChallenge.Token;
                _challengeStore.AddChallenge(challengeToken, keyAuthz);
                _logger.LogInformation("Published ACME challenge for {Domain} (token {Token})",
                    proxyHost.DomainName, challengeToken);

                await httpChallenge.Validate();

                // Poll the authorization until it is validated (or fails). The
                // authorization — not the order — reflects challenge progress.
                var maxAttempts = 15;
                var attempt = 0;
                Challenge challengeResource;
                do
                {
                    await Task.Delay(2000);
                    challengeResource = await httpChallenge.Resource();
                    attempt++;
                }
                while (challengeResource.Status != ChallengeStatus.Valid
                    && challengeResource.Status != ChallengeStatus.Invalid
                    && attempt < maxAttempts);

                if (challengeResource.Status != ChallengeStatus.Valid)
                {
                    _logger.LogError(
                        "ACME challenge for {Domain} did not validate (status {Status}): {Error}",
                        proxyHost.DomainName,
                        challengeResource.Status,
                        challengeResource.Error?.Detail);
                    return false;
                }

                var privateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);

                var cert = await order.Generate(new CsrInfo
                {
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
            finally
            {
                if (challengeToken != null)
                {
                    _challengeStore.RemoveChallenge(challengeToken);
                }
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

            // Pick up both hosts that still need an initial certificate
            // (SslCertificateExpiry == null) and hosts whose certificate is
            // within the 30-day renewal window.
            var pendingHosts = await _dbContext.Set<ProxyHost>()
                .Where(p => p.UseLetsEncrypt
                    && (p.SslCertificateExpiry == null || p.SslCertificateExpiry < expiringDate))
                .ToListAsync();

            foreach (var host in pendingHosts)
            {
                _logger.LogInformation(
                    "{Action} certificate for {Domain}",
                    host.SslCertificateExpiry == null ? "Issuing" : "Renewing",
                    host.DomainName);
                await RequestCertificateAsync(host);
            }
        }
    }
}
