using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StageZero.DataAdapters.DnsProviders;
using StageZero.DataAdapters.DnsRecords;
using StageZero.Models;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Dns;

/// <summary>Seeds a provider and records, and answers Cloudflare's DNS API in FakeHttp.</summary>
internal static class CloudflareFakes
{
    public const string Token = "cf-test-token-0123456789abcdefghijklmnop";
    public const string ZoneId = "zone-1";
    public const string ZoneUrl = $"https://api.cloudflare.com/client/v4/zones/{ZoneId}/dns_records";

    public static async Task<DnsProvider> AddProviderAsync(StageZeroApp app, params DnsRecord[] records)
    {
        using var scope = app.Services.CreateScope();
        var provider = await scope.ServiceProvider.GetRequiredService<IDnsProviderWriter>().InsertAsync(new DnsProvider
        {
            Name = "Home", ProviderType = "Cloudflare", ApiToken = Token, ZoneId = ZoneId
        });

        var writer = scope.ServiceProvider.GetRequiredService<IDnsRecordWriter>();
        foreach (var record in records)
        {
            record.DnsProviderId = provider.Id;
            await writer.InsertAsync(record);
        }

        return provider;
    }

    /// <summary>A Cloudflare list response ({ success, result: [...] }).</summary>
    public static string List(params object[] records) =>
        JsonSerializer.Serialize(new { success = true, result = records, result_info = new { page = 1, total_pages = 1 } });

    public static object Record(string id, string name, string type, string content, bool proxied = false, int ttl = 1) =>
        new { id, name, type, content, proxied, ttl };

    public static void AcceptUpdates(StageZeroApp app) =>
        app.Http.On(HttpMethod.Patch, ZoneUrl, _ => FakeHttp.Text("""{"success":true,"result":{}}"""));
}
