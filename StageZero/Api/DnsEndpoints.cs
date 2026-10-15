using StageZero.Models;
using StageZero.Services.Dns;

namespace StageZero.Api;

/// <summary>
/// DNS providers and the records StageZero tracks. A provider's Cloudflare token goes in
/// once, when it is added, and never comes back out: nothing here serialises it.
/// </summary>
public static class DnsEndpoints
{
    public static void MapDnsEndpoints(this IEndpointRouteBuilder api)
    {
        var dns = api.MapGroup("/dns").RequireAuthorization(Session.OperatorPolicy).WithUserFacingErrors();

        dns.MapGet("/providers", async (IDnsConfigService service) =>
            (await service.GetProvidersAsync()).Select(ProviderView.From));

        dns.MapPost("/providers", async (AddProviderRequest request, IDnsConfigService service) =>
            ProviderView.From(await service.AddProviderAsync(
                request.Name ?? string.Empty, request.ApiToken ?? string.Empty, request.ZoneId ?? string.Empty, request.IsActive)));

        dns.MapPost("/providers/{id:int}/toggle", async (int id, IDnsConfigService service) =>
            ProviderView.From(await service.ToggleProviderAsync(id)));

        dns.MapDelete("/providers/{id:int}", async (int id, IDnsConfigService service) =>
        {
            await service.DeleteProviderAsync(id);
            return Results.NoContent();
        });

        dns.MapPost("/providers/{id:int}/records", async (int id, DnsRecordInput input, IDnsConfigService service) =>
            RecordView.From(await service.AddRecordAsync(id, input)));

        dns.MapPut("/records/{id:int}", async (int id, DnsRecordInput input, IDnsConfigService service) =>
            RecordView.From(await service.UpdateRecordAsync(id, input)));

        dns.MapDelete("/records/{id:int}", async (int id, IDnsConfigService service) =>
        {
            await service.DeleteRecordAsync(id);
            return Results.NoContent();
        });

        // POST, so the token travels in the body and never in a URL or an access log.
        dns.MapPost("/zones", (ZonesRequest request, IDnsConfigService service) =>
            service.GetCloudflareZonesAsync(request.ApiToken ?? string.Empty));

        dns.MapGet("/providers/{id:int}/cloudflare-records", (int id, IDnsConfigService service) =>
            service.GetCloudflareRecordsAsync(id));

        dns.MapPost("/providers/{id:int}/import", (int id, IDnsConfigService service) =>
            service.ImportAllRecordsAsync(id));
    }

    public sealed record AddProviderRequest(string? Name, string? ApiToken, string? ZoneId, bool IsActive = true);

    public sealed record ZonesRequest(string? ApiToken);

    public sealed record ProviderView(
        int Id, string Name, string ProviderType, string? ZoneId, bool IsActive,
        DateTime CreatedAt, DateTime? LastUsedAt, IReadOnlyList<RecordView> Records)
    {
        public static ProviderView From(DnsProvider p) => new(
            p.Id, p.Name, p.ProviderType, p.ZoneId, p.IsActive, p.CreatedAt, p.LastUsedAt,
            p.DnsRecords.OrderBy(r => r.RecordName).Select(RecordView.From).ToList());
    }

    public sealed record RecordView(
        int Id, int ProviderId, string RecordName, string RecordType, string? RecordId, bool AutoUpdate,
        bool AutoUpdateSupported, string? AutoUpdateUnavailableReason,
        string? Content, string? LastIpAddress, DateTime? LastUpdatedAt)
    {
        public static RecordView From(DnsRecord r) => new(
            r.Id, r.DnsProviderId, r.RecordName, r.RecordType, r.RecordId, r.AutoUpdate,
            DnsRecord.SupportsAutoUpdate(r.RecordType), DnsRecord.AutoUpdateUnavailableReason(r.RecordType),
            r.Content, r.LastIpAddress, r.LastUpdatedAt);
    }
}
