using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StageZero.Models;

namespace StageZero.Services.Tunnel;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface ICloudflareTunnelService
{
    Task<List<TunnelInfo>> ListTunnelsAsync(string apiToken, string accountId);
    Task<TunnelInfo> CreateTunnelAsync(string apiToken, string accountId, string name);

    /// <summary>
    /// Switches an existing tunnel from local (config.yml) to remotely-managed
    /// configuration so StageZero can push ingress rules to it.
    /// </summary>
    Task AdoptTunnelAsync(string apiToken, string accountId, string tunnelId);

    /// <summary>The token passed to `cloudflared service install &lt;token&gt;` on the host.</summary>
    Task<string> GetConnectorTokenAsync(string apiToken, string accountId, string tunnelId);

    /// <summary>
    /// Replaces the tunnel's entire ingress rule set. Cloudflare has no delta API —
    /// every enabled route must be included on every call.
    /// </summary>
    Task SyncIngressAsync(string apiToken, string accountId, string tunnelId, IEnumerable<TunnelRoute> enabledRoutes);

    /// <summary>Creates or updates the proxied CNAME pointing a hostname at the tunnel.</summary>
    Task EnsureCnameAsync(string apiToken, string zoneId, string hostname, string tunnelId);

    /// <summary>Removes the tunnel CNAME for a hostname. No-op if it does not exist.</summary>
    Task RemoveCnameAsync(string apiToken, string zoneId, string hostname);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

public class TunnelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"cloudflare" for remotely-managed tunnels, "local" for config.yml tunnels.</summary>
    public string ConfigSrc { get; set; } = string.Empty;

    public string? Status { get; set; }

    public bool IsRemotelyManaged =>
        string.Equals(ConfigSrc, "cloudflare", StringComparison.OrdinalIgnoreCase);

    /// <summary>Hostname CNAMEs point at to reach this tunnel.</summary>
    public string CnameTarget => $"{Id}.cfargotunnel.com";
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class CloudflareTunnelServiceException : Exception
{
    public CloudflareTunnelServiceException(string message) : base(message) { }
    public CloudflareTunnelServiceException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class CloudflareTunnelService : ICloudflareTunnelService
{
    private readonly ILogger<CloudflareTunnelService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private const string CLOUDFLARE_API_BASE = "https://api.cloudflare.com/client/v4";

    /// <summary>Ingress rules must end with a catch-all; Cloudflare rejects the config otherwise.</summary>
    private const string CATCH_ALL_SERVICE = "http_status:404";

    public CloudflareTunnelService(
        ILogger<CloudflareTunnelService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    // ───────────────────────────────────────────────────────────
    // TUNNELS
    // ───────────────────────────────────────────────────────────

    public async Task<List<TunnelInfo>> ListTunnelsAsync(string apiToken, string accountId)
    {
        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.GetAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/cfd_tunnel?is_deleted=false");

            var root = await ReadResultAsync(response, "list tunnels");
            var tunnels = new List<TunnelInfo>();

            foreach (var tunnel in root.EnumerateArray())
            {
                tunnels.Add(ParseTunnel(tunnel));
            }

            return tunnels;
        }
        catch (CloudflareTunnelServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Cloudflare tunnels");
            throw new CloudflareTunnelServiceException("Could not list tunnels", ex);
        }
    }

    public async Task<TunnelInfo> CreateTunnelAsync(string apiToken, string accountId, string name)
    {
        try
        {
            _logger.LogInformation("Creating remotely-managed Cloudflare tunnel {TunnelName}", name);

            var httpClient = CreateAuthenticatedClient(apiToken);
            var payload = new
            {
                name,
                config_src = "cloudflare"
            };

            var response = await httpClient.PostAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/cfd_tunnel",
                JsonBody(payload));

            var root = await ReadResultAsync(response, "create tunnel");
            var created = ParseTunnel(root);

            _logger.LogInformation("Created tunnel {TunnelName} ({TunnelId})", created.Name, created.Id);
            return created;
        }
        catch (CloudflareTunnelServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Cloudflare tunnel {TunnelName}", name);
            throw new CloudflareTunnelServiceException("Could not create tunnel", ex);
        }
    }

    public async Task AdoptTunnelAsync(string apiToken, string accountId, string tunnelId)
    {
        try
        {
            _logger.LogInformation("Switching tunnel {TunnelId} to remotely-managed configuration", tunnelId);

            var httpClient = CreateAuthenticatedClient(apiToken);
            var payload = new { config_src = "cloudflare" };

            var response = await httpClient.PatchAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/cfd_tunnel/{tunnelId}",
                JsonBody(payload));

            await ReadResultAsync(response, "adopt tunnel");
        }
        catch (CloudflareTunnelServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adopting Cloudflare tunnel {TunnelId}", tunnelId);
            throw new CloudflareTunnelServiceException("Could not adopt tunnel", ex);
        }
    }

    public async Task<string> GetConnectorTokenAsync(string apiToken, string accountId, string tunnelId)
    {
        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);
            var response = await httpClient.GetAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/cfd_tunnel/{tunnelId}/token");

            var root = await ReadResultAsync(response, "get connector token");
            var token = root.ValueKind == JsonValueKind.String ? root.GetString() : null;

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new CloudflareTunnelServiceException("Cloudflare returned an empty connector token");
            }

            return token;
        }
        catch (CloudflareTunnelServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting connector token for tunnel {TunnelId}", tunnelId);
            throw new CloudflareTunnelServiceException("Could not get connector token", ex);
        }
    }

    // ───────────────────────────────────────────────────────────
    // INGRESS
    // ───────────────────────────────────────────────────────────

    public async Task SyncIngressAsync(
        string apiToken,
        string accountId,
        string tunnelId,
        IEnumerable<TunnelRoute> enabledRoutes)
    {
        try
        {
            // Cloudflare matches ingress rules top-down and requires a catch-all last.
            var ingress = enabledRoutes
                .Select(route => new Dictionary<string, object>
                {
                    ["hostname"] = route.DomainName,
                    ["service"] = route.ForwardUrl
                })
                .ToList();

            ingress.Add(new Dictionary<string, object> { ["service"] = CATCH_ALL_SERVICE });

            _logger.LogInformation(
                "Pushing {RuleCount} ingress rule(s) to tunnel {TunnelId}",
                ingress.Count - 1, tunnelId);

            var httpClient = CreateAuthenticatedClient(apiToken);
            var payload = new { config = new { ingress } };

            var response = await httpClient.PutAsync(
                $"{CLOUDFLARE_API_BASE}/accounts/{accountId}/cfd_tunnel/{tunnelId}/configurations",
                JsonBody(payload));

            await ReadResultAsync(response, "sync ingress rules");
        }
        catch (CloudflareTunnelServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing ingress rules for tunnel {TunnelId}", tunnelId);
            throw new CloudflareTunnelServiceException("Could not sync ingress rules", ex);
        }
    }

    // ───────────────────────────────────────────────────────────
    // DNS (tunnel CNAMEs)
    // ───────────────────────────────────────────────────────────

    public async Task EnsureCnameAsync(string apiToken, string zoneId, string hostname, string tunnelId)
    {
        try
        {
            var target = $"{tunnelId}.cfargotunnel.com";
            var httpClient = CreateAuthenticatedClient(apiToken);
            var existingId = await FindDnsRecordIdAsync(httpClient, zoneId, hostname, "CNAME");

            var payload = new
            {
                type = "CNAME",
                name = hostname,
                content = target,
                ttl = 1,       // automatic
                proxied = true // required for tunnel hostnames
            };

            HttpResponseMessage response;
            if (existingId is null)
            {
                _logger.LogInformation("Creating tunnel CNAME {Hostname} -> {Target}", hostname, target);
                response = await httpClient.PostAsync(
                    $"{CLOUDFLARE_API_BASE}/zones/{zoneId}/dns_records",
                    JsonBody(payload));
            }
            else
            {
                _logger.LogInformation("Updating tunnel CNAME {Hostname} -> {Target}", hostname, target);
                response = await httpClient.PutAsync(
                    $"{CLOUDFLARE_API_BASE}/zones/{zoneId}/dns_records/{existingId}",
                    JsonBody(payload));
            }

            await ReadResultAsync(response, $"write CNAME for {hostname}");
        }
        catch (CloudflareTunnelServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing tunnel CNAME for {Hostname}", hostname);
            throw new CloudflareTunnelServiceException($"Could not write CNAME for {hostname}", ex);
        }
    }

    public async Task RemoveCnameAsync(string apiToken, string zoneId, string hostname)
    {
        try
        {
            var httpClient = CreateAuthenticatedClient(apiToken);
            var existingId = await FindDnsRecordIdAsync(httpClient, zoneId, hostname, "CNAME");

            if (existingId is null)
            {
                _logger.LogDebug("No CNAME found for {Hostname}; nothing to remove", hostname);
                return;
            }

            _logger.LogInformation("Removing tunnel CNAME {Hostname}", hostname);
            var response = await httpClient.DeleteAsync(
                $"{CLOUDFLARE_API_BASE}/zones/{zoneId}/dns_records/{existingId}");

            await ReadResultAsync(response, $"delete CNAME for {hostname}");
        }
        catch (CloudflareTunnelServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing tunnel CNAME for {Hostname}", hostname);
            throw new CloudflareTunnelServiceException($"Could not remove CNAME for {hostname}", ex);
        }
    }

    // ───────────────────────────────────────────────────────────
    // HELPERS
    // ───────────────────────────────────────────────────────────

    private static TunnelInfo ParseTunnel(JsonElement tunnel)
    {
        return new TunnelInfo
        {
            Id = tunnel.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Name = tunnel.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            ConfigSrc = tunnel.TryGetProperty("config_src", out var src) ? src.GetString() ?? "" : "",
            Status = tunnel.TryGetProperty("status", out var status) ? status.GetString() : null
        };
    }

    private async Task<string?> FindDnsRecordIdAsync(
        HttpClient httpClient,
        string zoneId,
        string name,
        string type)
    {
        var response = await httpClient.GetAsync(
            $"{CLOUDFLARE_API_BASE}/zones/{zoneId}/dns_records?name={Uri.EscapeDataString(name)}&type={type}");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);

        if (doc.RootElement.TryGetProperty("result", out var result)
            && result.ValueKind == JsonValueKind.Array)
        {
            var firstRecord = result.EnumerateArray().FirstOrDefault();
            if (firstRecord.ValueKind != JsonValueKind.Undefined)
            {
                return firstRecord.GetProperty("id").GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Validates a Cloudflare response and returns a clone of its "result" element.
    /// The clone outlives the JsonDocument, so callers can read it after disposal.
    /// </summary>
    private async Task<JsonElement> ReadResultAsync(HttpResponseMessage response, string operation)
    {
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var detail = ExtractErrorMessage(responseBody);
            _logger.LogError("Cloudflare API failed to {Operation}: {StatusCode} {Detail}",
                operation, response.StatusCode, detail);
            throw new CloudflareTunnelServiceException(
                $"Cloudflare API error while trying to {operation}: {response.StatusCode} — {detail}");
        }

        using var doc = JsonDocument.Parse(responseBody);

        // Cloudflare returns 200 with success:false for some validation failures.
        if (doc.RootElement.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            var detail = ExtractErrorMessage(responseBody);
            _logger.LogError("Cloudflare API rejected {Operation}: {Detail}", operation, detail);
            throw new CloudflareTunnelServiceException(
                $"Cloudflare rejected the request to {operation}: {detail}");
        }

        return doc.RootElement.TryGetProperty("result", out var result)
            ? result.Clone()
            : default;
    }

    private static string ExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList();

                if (messages.Count > 0)
                {
                    return string.Join("; ", messages);
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body below.
        }

        return string.IsNullOrWhiteSpace(responseBody) ? "no detail returned" : responseBody;
    }

    private static StringContent JsonBody(object payload)
    {
        return new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
    }

    private HttpClient CreateAuthenticatedClient(string apiToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        return httpClient;
    }
}
