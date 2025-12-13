# PowerShell script to generate all reverse proxy library files
# Run this in your solution directory

# Detect project name from .sln file or use folder name
$solutionFile = Get-ChildItem -Filter "*.sln" | Select-Object -First 1
if ($solutionFile) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($solutionFile.Name)
    Write-Host "Detected solution: $projectName" -ForegroundColor Green
} else {
    $projectName = Split-Path -Leaf (Get-Location)
    Write-Host "No .sln found, using folder name: $projectName" -ForegroundColor Yellow
}

$libraryNamespace = "$projectName.ReverseProxy"
$libraryFolder = "$projectName.ReverseProxy"

Write-Host "Library namespace will be: $libraryNamespace" -ForegroundColor Cyan
Write-Host ""

$confirmation = Read-Host "Is this correct? (Y/n)"
if ($confirmation -eq 'n' -or $confirmation -eq 'N') {
    $projectName = Read-Host "Enter your project name"
    $libraryNamespace = "$projectName.ReverseProxy"
    $libraryFolder = "$projectName.ReverseProxy"
}

Write-Host ""
Write-Host "Creating files with namespace: $libraryNamespace" -ForegroundColor Green
Write-Host ""

$files = @{
    "$libraryFolder/Models/ProxyHost.cs" = @"
using System;
using System.ComponentModel.DataAnnotations;

namespace $libraryNamespace.Models
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

        public string ForwardUrl => `$"{ForwardScheme}://{ForwardHost}:{ForwardPort}";
    }
}
"@

    "$libraryFolder/Services/ProxyHostManager.cs" = @"
using Microsoft.EntityFrameworkCore;
using $libraryNamespace.Models;

namespace $libraryNamespace.Services
{
    public interface IProxyHostManager
    {
        Task<List<ProxyHost>> GetAllAsync();
        Task<ProxyHost?> GetByIdAsync(int id);
        Task<ProxyHost?> GetByDomainAsync(string domain);
        Task<ProxyHost> CreateAsync(ProxyHost proxyHost);
        Task<ProxyHost> UpdateAsync(ProxyHost proxyHost);
        Task<bool> DeleteAsync(int id);
        Task<bool> ToggleEnabledAsync(int id);
    }

    public class ProxyHostManager : IProxyHostManager
    {
        private readonly DbContext _dbContext;
        private readonly IProxyConfigurationService _proxyConfigService;

        public ProxyHostManager(DbContext dbContext, IProxyConfigurationService proxyConfigService)
        {
            _dbContext = dbContext;
            _proxyConfigService = proxyConfigService;
        }

        public async Task<List<ProxyHost>> GetAllAsync()
        {
            return await _dbContext.Set<ProxyHost>()
                .OrderBy(p => p.DomainName)
                .ToListAsync();
        }

        public async Task<ProxyHost?> GetByIdAsync(int id)
        {
            return await _dbContext.Set<ProxyHost>().FindAsync(id);
        }

        public async Task<ProxyHost?> GetByDomainAsync(string domain)
        {
            return await _dbContext.Set<ProxyHost>()
                .FirstOrDefaultAsync(p => p.DomainName == domain);
        }

        public async Task<ProxyHost> CreateAsync(ProxyHost proxyHost)
        {
            var existing = await GetByDomainAsync(proxyHost.DomainName);
            if (existing != null)
            {
                throw new InvalidOperationException(`$"A proxy host for domain '{proxyHost.DomainName}' already exists.");
            }

            proxyHost.CreatedAt = DateTime.UtcNow;
            proxyHost.UpdatedAt = DateTime.UtcNow;

            _dbContext.Set<ProxyHost>().Add(proxyHost);
            await _dbContext.SaveChangesAsync();

            await _proxyConfigService.ReloadConfigurationAsync();

            return proxyHost;
        }

        public async Task<ProxyHost> UpdateAsync(ProxyHost proxyHost)
        {
            var existing = await GetByIdAsync(proxyHost.Id);
            if (existing == null)
            {
                throw new InvalidOperationException(`$"Proxy host with ID {proxyHost.Id} not found.");
            }

            var duplicate = await _dbContext.Set<ProxyHost>()
                .FirstOrDefaultAsync(p => p.DomainName == proxyHost.DomainName && p.Id != proxyHost.Id);
            
            if (duplicate != null)
            {
                throw new InvalidOperationException(`$"A proxy host for domain '{proxyHost.DomainName}' already exists.");
            }

            proxyHost.UpdatedAt = DateTime.UtcNow;
            _dbContext.Entry(existing).CurrentValues.SetValues(proxyHost);
            await _dbContext.SaveChangesAsync();

            await _proxyConfigService.ReloadConfigurationAsync();

            return proxyHost;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var proxyHost = await GetByIdAsync(id);
            if (proxyHost == null)
            {
                return false;
            }

            _dbContext.Set<ProxyHost>().Remove(proxyHost);
            await _dbContext.SaveChangesAsync();

            await _proxyConfigService.ReloadConfigurationAsync();

            return true;
        }

        public async Task<bool> ToggleEnabledAsync(int id)
        {
            var proxyHost = await GetByIdAsync(id);
            if (proxyHost == null)
            {
                return false;
            }

            proxyHost.IsEnabled = !proxyHost.IsEnabled;
            proxyHost.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            await _proxyConfigService.ReloadConfigurationAsync();

            return true;
        }
    }
}
"@

    "$libraryFolder/Services/DatabaseProxyConfigProvider.cs" = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using $libraryNamespace.Models;

namespace $libraryNamespace.Services
{
    public interface IProxyConfigurationService
    {
        Task ReloadConfigurationAsync();
    }

    public class DatabaseProxyConfigProvider : IProxyConfigProvider, IProxyConfigurationService
    {
        private readonly DbContext _dbContext;
        private volatile InMemoryConfigProvider _configProvider;
        private readonly object _lock = new object();

        public DatabaseProxyConfigProvider(DbContext dbContext)
        {
            _dbContext = dbContext;
            _configProvider = new InMemoryConfigProvider(new List<RouteConfig>(), new List<ClusterConfig>());
        }

        public IProxyConfig GetConfig()
        {
            return _configProvider.GetConfig();
        }

        public async Task ReloadConfigurationAsync()
        {
            var proxyHosts = await _dbContext.Set<ProxyHost>()
                .Where(p => p.IsEnabled)
                .ToListAsync();

            var routes = new List<RouteConfig>();
            var clusters = new List<ClusterConfig>();

            foreach (var host in proxyHosts)
            {
                var routeId = `$"route_{host.Id}";
                var clusterId = `$"cluster_{host.Id}";

                var route = new RouteConfig
                {
                    RouteId = routeId,
                    ClusterId = clusterId,
                    Match = new RouteMatch
                    {
                        Hosts = new[] { host.DomainName }
                    }
                };

                var metadata = new Dictionary<string, string>();
                
                if (host.WebSocketsSupport)
                {
                    metadata["WebSocketsSupport"] = "true";
                }

                var cluster = new ClusterConfig
                {
                    ClusterId = clusterId,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        {
                            `$"destination_{host.Id}",
                            new DestinationConfig
                            {
                                Address = `$"{host.ForwardScheme}://{host.ForwardHost}:{host.ForwardPort}"
                            }
                        }
                    },
                    Metadata = metadata
                };

                routes.Add(route);
                clusters.Add(cluster);
            }

            lock (_lock)
            {
                var oldConfig = _configProvider;
                _configProvider = new InMemoryConfigProvider(routes, clusters);
                oldConfig.SignalChange();
            }
        }

        private class InMemoryConfigProvider : IProxyConfig
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public InMemoryConfigProvider(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
            {
                Routes = routes;
                Clusters = clusters;
                ChangeToken = new CancellationChangeToken(_cts.Token);
            }

            public IReadOnlyList<RouteConfig> Routes { get; }
            public IReadOnlyList<ClusterConfig> Clusters { get; }
            public IChangeToken ChangeToken { get; }

            internal void SignalChange()
            {
                _cts.Cancel();
            }
        }
    }
}
"@

    "$libraryFolder/Services/CertificateService.cs" = @"
using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using $libraryNamespace.Models;

namespace $libraryNamespace.Services
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

                var certPath = Path.Combine(_certificatesPath, `$"{proxyHost.DomainName}.crt");
                var keyPath = Path.Combine(_certificatesPath, `$"{proxyHost.DomainName}.key");

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
"@

    "$libraryFolder/Services/CertificateRenewalBackgroundService.cs" = @"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace $libraryNamespace.Services
{
    public class CertificateRenewalBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CertificateRenewalBackgroundService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

        public CertificateRenewalBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<CertificateRenewalBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Certificate Renewal Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Checking for certificates that need renewal");

                    using var scope = _serviceProvider.CreateScope();
                    var certificateService = scope.ServiceProvider.GetRequiredService<ICertificateService>();

                    await certificateService.RenewExpiringCertificatesAsync();

                    _logger.LogInformation("Certificate renewal check completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during certificate renewal check");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Certificate Renewal Background Service stopped");
        }
    }
}
"@

    "$libraryFolder/Extensions/ServiceCollectionExtensions.cs" = @"
using Microsoft.Extensions.DependencyInjection;
using $libraryNamespace.Services;

namespace $libraryNamespace.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddReverseProxyManagement(this IServiceCollection services)
        {
            var configProvider = services.BuildServiceProvider().GetRequiredService<DatabaseProxyConfigProvider>();
            
            services.AddSingleton<DatabaseProxyConfigProvider>(configProvider);
            services.AddSingleton<IProxyConfigurationService>(sp => sp.GetRequiredService<DatabaseProxyConfigProvider>());
            
            services.AddReverseProxy()
                .LoadFromMemory(new List<Yarp.ReverseProxy.Configuration.RouteConfig>(), 
                               new List<Yarp.ReverseProxy.Configuration.ClusterConfig>())
                .AddConfigFilter<ProxyConfigFilter>();

            services.AddScoped<IProxyHostManager, ProxyHostManager>();
            services.AddScoped<ICertificateService, CertificateService>();
            
            services.AddHostedService<CertificateRenewalBackgroundService>();

            return services;
        }
    }

    public class ProxyConfigFilter : IProxyConfigFilter
    {
        public ValueTask<Yarp.ReverseProxy.Configuration.ClusterConfig> ConfigureClusterAsync(
            Yarp.ReverseProxy.Configuration.ClusterConfig cluster, 
            CancellationToken cancel)
        {
            return new ValueTask<Yarp.ReverseProxy.Configuration.ClusterConfig>(cluster);
        }

        public ValueTask<Yarp.ReverseProxy.Configuration.RouteConfig> ConfigureRouteAsync(
            Yarp.ReverseProxy.Configuration.RouteConfig route, 
            Yarp.ReverseProxy.Configuration.ClusterConfig? cluster, 
            CancellationToken cancel)
        {
            return new ValueTask<Yarp.ReverseProxy.Configuration.RouteConfig>(route);
        }
    }
}
"@

    "$libraryFolder/$libraryFolder.csproj" = @"
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Yarp.ReverseProxy" Version="2.1.0" />
    <PackageReference Include="Certes" Version="3.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="8.0.0" />
  </ItemGroup>

</Project>
"@

    "BlazorUIPages/ProxyHosts.razor" = @"
@page "/admin/proxy-hosts"
@using $libraryNamespace.Models
@using $libraryNamespace.Services
@inject IProxyHostManager ProxyManager
@inject NavigationManager Navigation
@inject IJSRuntime JSRuntime

<PageTitle>Proxy Hosts</PageTitle>

<h1>Reverse Proxy Management</h1>

<div class="mb-3">
    <button class="btn btn-primary" @onclick="NavigateToAdd">
        <i class="bi bi-plus-circle"></i> Add Proxy Host
    </button>
</div>

@if (proxyHosts == null)
{
    <p><em>Loading...</em></p>
}
else if (!proxyHosts.Any())
{
    <div class="alert alert-info">
        No proxy hosts configured yet. Click "Add Proxy Host" to create your first one.
    </div>
}
else
{
    <div class="table-responsive">
        <table class="table table-striped table-hover">
            <thead>
                <tr>
                    <th>Domain</th>
                    <th>Forward To</th>
                    <th>SSL</th>
                    <th>Status</th>
                    <th>Actions</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var host in proxyHosts)
                {
                    <tr>
                        <td>
                            <strong>@host.DomainName</strong>
                            @if (!string.IsNullOrEmpty(host.Notes))
                            {
                                <br />
                                <small class="text-muted">@host.Notes</small>
                            }
                        </td>
                        <td>
                            <code>@host.ForwardUrl</code>
                            @if (host.WebSocketsSupport)
                            {
                                <br />
                                <span class="badge bg-info">WebSockets</span>
                            }
                        </td>
                        <td>
                            @if (host.SslEnabled)
                            {
                                <span class="badge bg-success">
                                    <i class="bi bi-shield-check"></i> Enabled
                                </span>
                                @if (host.SslCertificateExpiry.HasValue)
                                {
                                    <br />
                                    <small>Expires: @host.SslCertificateExpiry.Value.ToString("yyyy-MM-dd")</small>
                                }
                            }
                            else
                            {
                                <span class="badge bg-secondary">Disabled</span>
                            }
                        </td>
                        <td>
                            @if (host.IsEnabled)
                            {
                                <span class="badge bg-success">Active</span>
                            }
                            else
                            {
                                <span class="badge bg-warning">Disabled</span>
                            }
                        </td>
                        <td>
                            <div class="btn-group btn-group-sm" role="group">
                                <button class="btn btn-outline-primary" @onclick="() => NavigateToEdit(host.Id)">
                                    <i class="bi bi-pencil"></i> Edit
                                </button>
                                <button class="btn btn-outline-secondary" @onclick="() => ToggleEnabled(host.Id)">
                                    <i class="bi bi-power"></i> @(host.IsEnabled ? "Disable" : "Enable")
                                </button>
                                <button class="btn btn-outline-danger" @onclick="() => DeleteHost(host.Id)">
                                    <i class="bi bi-trash"></i> Delete
                                </button>
                            </div>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
}

@code {
    private List<ProxyHost>? proxyHosts;

    protected override async Task OnInitializedAsync()
    {
        await LoadProxyHosts();
    }

    private async Task LoadProxyHosts()
    {
        proxyHosts = await ProxyManager.GetAllAsync();
    }

    private void NavigateToAdd()
    {
        Navigation.NavigateTo("/admin/proxy-hosts/add");
    }

    private void NavigateToEdit(int id)
    {
        Navigation.NavigateTo(`$"/admin/proxy-hosts/edit/{id}");
    }

    private async Task ToggleEnabled(int id)
    {
        await ProxyManager.ToggleEnabledAsync(id);
        await LoadProxyHosts();
    }

    private async Task DeleteHost(int id)
    {
        if (await JSRuntime.InvokeAsync<bool>("confirm", "Are you sure you want to delete this proxy host?"))
        {
            await ProxyManager.DeleteAsync(id);
            await LoadProxyHosts();
        }
    }
}
"@

    "BlazorUIPages/ProxyHostEdit.razor" = @"
@page "/admin/proxy-hosts/add"
@page "/admin/proxy-hosts/edit/{Id:int}"
@using $libraryNamespace.Models
@using $libraryNamespace.Services
@inject IProxyHostManager ProxyManager
@inject NavigationManager Navigation

<PageTitle>@(Id.HasValue ? "Edit" : "Add") Proxy Host</PageTitle>

<h1>@(Id.HasValue ? "Edit" : "Add") Proxy Host</h1>

<EditForm Model="@proxyHost" OnValidSubmit="@HandleSubmit">
    <DataAnnotationsValidator />
    <ValidationSummary />

    @if (!string.IsNullOrEmpty(errorMessage))
    {
        <div class="alert alert-danger">@errorMessage</div>
    }

    <div class="row">
        <div class="col-md-6">
            <h4>Domain Configuration</h4>
            
            <div class="mb-3">
                <label class="form-label">Domain Name</label>
                <InputText @bind-Value="proxyHost.DomainName" class="form-control" placeholder="app.yourdomain.com" />
                <div class="form-text">The subdomain this proxy will respond to</div>
            </div>

            <div class="mb-3">
                <label class="form-label">Notes (Optional)</label>
                <InputTextArea @bind-Value="proxyHost.Notes" class="form-control" rows="2" />
            </div>

            <h4 class="mt-4">Forward Configuration</h4>

            <div class="mb-3">
                <label class="form-label">Scheme</label>
                <InputSelect @bind-Value="proxyHost.ForwardScheme" class="form-select">
                    <option value="http">HTTP</option>
                    <option value="https">HTTPS</option>
                </InputSelect>
            </div>

            <div class="mb-3">
                <label class="form-label">Forward Host</label>
                <InputText @bind-Value="proxyHost.ForwardHost" class="form-control" placeholder="192.168.1.100 or myapp" />
                <div class="form-text">IP address, hostname, or Docker container name</div>
            </div>

            <div class="mb-3">
                <label class="form-label">Forward Port</label>
                <InputNumber @bind-Value="proxyHost.ForwardPort" class="form-control" />
            </div>

            <div class="form-check mb-3">
                <InputCheckbox @bind-Value="proxyHost.WebSocketsSupport" class="form-check-input" id="websockets" />
                <label class="form-check-label" for="websockets">
                    WebSockets Support
                </label>
            </div>

            <div class="form-check mb-3">
                <InputCheckbox @bind-Value="proxyHost.CacheAssets" class="form-check-input" id="cache" />
                <label class="form-check-label" for="cache">
                    Cache Assets
                </label>
            </div>

            <div class="form-check mb-3">
                <InputCheckbox @bind-Value="proxyHost.BlockCommonExploits" class="form-check-input" id="exploits" />
                <label class="form-check-label" for="exploits">
                    Block Common Exploits
                </label>
            </div>
        </div>

        <div class="col-md-6">
            <h4>SSL Configuration</h4>

            <div class="form-check mb-3">
                <InputCheckbox @bind-Value="proxyHost.SslEnabled" class="form-check-input" id="sslEnabled" />
                <label class="form-check-label" for="sslEnabled">
                    <strong>Enable SSL</strong>
                </label>
            </div>

            @if (proxyHost.SslEnabled)
            {
                <div class="form-check mb-3">
                    <InputCheckbox @bind-Value="proxyHost.SslForced" class="form-check-input" id="forceSSL" />
                    <label class="form-check-label" for="forceSSL">
                        Force SSL (Redirect HTTP to HTTPS)
                    </label>
                </div>

                <div class="form-check mb-3">
                    <InputCheckbox @bind-Value="proxyHost.Http2Support" class="form-check-input" id="http2" />
                    <label class="form-check-label" for="http2">
                        HTTP/2 Support
                    </label>
                </div>

                <div class="form-check mb-3">
                    <InputCheckbox @bind-Value="proxyHost.HstsEnabled" class="form-check-input" id="hsts" />
                    <label class="form-check-label" for="hsts">
                        HSTS Enabled
                    </label>
                </div>

                @if (proxyHost.HstsEnabled)
                {
                    <div class="mb-3">
                        <label class="form-label">HSTS Max Age (seconds)</label>
                        <InputNumber @bind-Value="proxyHost.HstsMaxAge" class="form-control" />
                        <div class="form-text">Default: 31536000 (1 year)</div>
                    </div>
                }

                <hr />

                <h5>Let's Encrypt</h5>

                <div class="form-check mb-3">
                    <InputCheckbox @bind-Value="proxyHost.UseLetsEncrypt" class="form-check-input" id="letsencrypt" />
                    <label class="form-check-label" for="letsencrypt">
                        Request Let's Encrypt Certificate
                    </label>
                </div>

                @if (proxyHost.UseLetsEncrypt)
                {
                    <div class="mb-3">
                        <label class="form-label">Email for Let's Encrypt</label>
                        <InputText @bind-Value="proxyHost.LetsEncryptEmail" class="form-control" type="email" />
                        <div class="form-text">Used for certificate expiration notices</div>
                    </div>

                    <div class="alert alert-info">
                        <strong>Note:</strong> Make sure ports 80 and 443 are forwarded to this server and DNS is properly configured before requesting a certificate.
                    </div>
                }
            }

            <h4 class="mt-4">Status</h4>

            <div class="form-check mb-3">
                <InputCheckbox @bind-Value="proxyHost.IsEnabled" class="form-check-input" id="enabled" />
                <label class="form-check-label" for="enabled">
                    <strong>Enabled</strong>
                </label>
            </div>
        </div>
    </div>

    <div class="mt-4">
        <button type="submit" class="btn btn-primary" disabled="@isSubmitting">
            @if (isSubmitting)
            {
                <span class="spinner-border spinner-border-sm me-2"></span>
            }
            @(Id.HasValue ? "Update" : "Create") Proxy Host
        </button>
        <button type="button" class="btn btn-secondary" @onclick="Cancel">Cancel</button>
    </div>
</EditForm>

@code {
    [Parameter]
    public int? Id { get; set; }

    private ProxyHost proxyHost = new ProxyHost
    {
        ForwardScheme = "http",
        ForwardPort = 8080,
        BlockCommonExploits = true,
        Http2Support = true,
        HstsMaxAge = 31536000,
        IsEnabled = true
    };

    private bool isSubmitting = false;
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        if (Id.HasValue)
        {
            var existing = await ProxyManager.GetByIdAsync(Id.Value);
            if (existing != null)
            {
                proxyHost = existing;
            }
            else
            {
                Navigation.NavigateTo("/admin/proxy-hosts");
            }
        }
    }

    private async Task HandleSubmit()
    {
        isSubmitting = true;
        errorMessage = null;

        try
        {
            if (Id.HasValue)
            {
                await ProxyManager.UpdateAsync(proxyHost);
            }
            else
            {
                await ProxyManager.CreateAsync(proxyHost);
            }

            Navigation.NavigateTo("/admin/proxy-hosts");
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private void Cancel()
    {
        Navigation.NavigateTo("/admin/proxy-hosts");
    }
}
"@

    "INSTALLATION.md" = @"
# Reverse Proxy Library - Installation Guide

Project Name: $projectName
Library Namespace: $libraryNamespace

## Step 1: Create the Class Library Project

```bash
dotnet new classlib -n $libraryFolder
dotnet sln add $libraryFolder
dotnet add $projectName reference $libraryFolder
```

## Step 2: Files Already Created

This script has created:
- $libraryFolder/ - All library files with correct namespaces
- BlazorUIPages/ - Razor pages ready to copy

## Step 3: Copy UI Pages

Copy the Razor files from BlazorUIPages/ to your Blazor project:
```
BlazorUIPages/ProxyHosts.razor     → $projectName/Pages/Admin/
BlazorUIPages/ProxyHostEdit.razor  → $projectName/Pages/Admin/
```

## Step 4: Update Your Blazor Project

Add package references to your $projectName.csproj:
```xml
<ItemGroup>
  <PackageReference Include="Yarp.ReverseProxy" Version="2.1.0" />
  <PackageReference Include="Certes" Version="3.0.0" />
</ItemGroup>
```

## Step 5: Update Your DbContext

Add to your existing DbContext class:
```csharp
using $libraryNamespace.Models;

public class YourDbContext : DbContext
{
    // Add this:
    public DbSet<ProxyHost> ProxyHosts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Add this:
        modelBuilder.Entity<ProxyHost>(entity =>
        {
            entity.HasIndex(e => e.DomainName).IsUnique();
            entity.HasIndex(e => e.IsEnabled);
        });
    }
}
```

## Step 6: Update Program.cs

```csharp
using $libraryNamespace.Extensions;
using $libraryNamespace.Services;

// Register DbContext as base class
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<YourDbContext>());

// Register config provider as singleton
builder.Services.AddSingleton<DatabaseProxyConfigProvider>();

// Add reverse proxy management
builder.Services.AddReverseProxyManagement();

// After app.Build(), initialize config
using (var scope = app.Services.CreateScope())
{
    var proxyConfigService = scope.ServiceProvider.GetRequiredService<IProxyConfigurationService>();
    await proxyConfigService.ReloadConfigurationAsync();
}

// Add BEFORE MapBlazorHub
app.MapReverseProxy();
```

## Step 7: Create and Run Migration

```bash
dotnet ef migrations add AddProxyHosts
dotnet ef database update
```

## Step 8: Run and Test

Navigate to: /admin/proxy-hosts
"@

    "README.md" = @"
# $libraryFolder

A complete reverse proxy library built with YARP for $projectName

## Namespaces

- $libraryNamespace.Models
- $libraryNamespace.Services  
- $libraryNamespace.Extensions

## Features

- Dynamic subdomain routing
- Let's Encrypt SSL automation
- WebSocket support
- Hot-reload configuration
- Blazor admin UI
- Auto certificate renewal

## Quick Start

Files have been generated in:
- $libraryFolder/ - Library files
- BlazorUIPages/ - UI pages to copy

Follow INSTALLATION.md for integration steps.
"@
}

Write-Host "Creating reverse proxy library files..." -ForegroundColor Green
Write-Host ""

foreach ($file in $files.Keys) {
    $directory = Split-Path -Path $file -Parent
    
    if ($directory -and !(Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        Write-Host "Created directory: $directory" -ForegroundColor Yellow
    }
    
    $files[$file] | Out-File -FilePath $file -Encoding UTF8
    Write-Host "Created: $file" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "All files created successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Files created:" -ForegroundColor Cyan
Write-Host "  - Library files in: $libraryFolder/" -ForegroundColor White
Write-Host "  - Blazor UI pages in: BlazorUIPages/" -ForegroundColor White
Write-Host "  - Namespaces use: $libraryNamespace" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Copy BlazorUIPages/*.razor to your $projectName/Pages/Admin/ folder" -ForegroundColor White
Write-Host "2. Create the class library project:" -ForegroundColor White
Write-Host "   dotnet new classlib -n $libraryFolder" -ForegroundColor Gray
Write-Host "3. Add the library reference to $projectName" -ForegroundColor White
Write-Host "4. Follow the INSTALLATION.md guide" -ForegroundColor White
Write-Host ""
Write-Host "For detailed instructions, see INSTALLATION.md" -ForegroundColor Yellow
