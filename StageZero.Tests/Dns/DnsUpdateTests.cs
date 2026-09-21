using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StageZero.Models;
using StageZero.Services.IpMonitoring;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Dns;

/// <summary>An IP change updates the record's address and nothing else about it.</summary>
[Collection(AppCollection.Name)]
public class DnsUpdateTests
{
    [Fact]
    public async Task An_ip_change_patches_only_the_address_and_keeps_the_proxy_on()
    {
        await using var app = new StageZeroApp();
        await CloudflareFakes.AddProviderAsync(app, new DnsRecord
        {
            RecordName = "home.example.com", RecordType = "A", RecordId = "rec-1", AutoUpdate = true
        });
        app.Http.OnGet("https://api.ipify.org", "203.0.113.2");
        app.Http.OnGet(CloudflareFakes.ZoneUrl, CloudflareFakes.List(
            CloudflareFakes.Record("rec-1", "home.example.com", "A", "203.0.113.1", proxied: true, ttl: 300)));
        CloudflareFakes.AcceptUpdates(app);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIpMonitorService>().CheckIpAsync();
        }

        var patch = Assert.Single(app.Http.Requests, r => r.Method == HttpMethod.Patch);
        Assert.Equal($"{CloudflareFakes.ZoneUrl}/rec-1", patch.Url.ToString());
        Assert.Equal($"Bearer {CloudflareFakes.Token}", patch.Authorization); // decrypted end to end

        using var body = JsonDocument.Parse(patch.Body!);
        var field = Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal(("content", "203.0.113.2"), (field.Name, field.Value.GetString()));
        Assert.DoesNotContain(app.Http.Requests, r => r.Method == HttpMethod.Put);
    }
}
