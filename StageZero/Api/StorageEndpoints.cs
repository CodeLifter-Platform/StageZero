using StageZero.Services.Dns;
using StageZero.Services.Storage;

namespace StageZero.Api;

/// <summary>
/// R2 buckets and the hostnames in front of them. Nothing secret crosses this boundary: the
/// token stays on the server, and objects (which need R2's S3 credentials) are not here.
/// </summary>
public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this IEndpointRouteBuilder api)
    {
        var storage = api.MapGroup("/storage").RequireAuthorization(Session.OperatorPolicy).WithUserFacingErrors();

        storage.MapGet("/options", () => new OptionsView(StorageService.LocationHints, StorageService.StorageClasses, StorageService.MinTlsVersions));

        // ── buckets ──
        var buckets = storage.MapGroup("/buckets");

        buckets.MapGet("/", async (IStorageService service) =>
            new BucketsResponse((await service.GetBucketsAsync()).Select(BucketView.From).ToList()));

        buckets.MapPost("/", async (CreateBucketRequest request, IStorageService service) =>
            BucketView.From(await service.CreateBucketAsync(request.Name ?? string.Empty, request.LocationHint, request.StorageClass)));

        buckets.MapDelete("/{bucket}", async (string bucket, IStorageService service) =>
        {
            await service.DeleteBucketAsync(bucket);
            return Results.NoContent();
        });

        // ── domains ──
        buckets.MapGet("/{bucket}/domains", async (string bucket, IStorageService service) =>
            DomainsView.From(await service.GetDomainsAsync(bucket)));

        buckets.MapPut("/{bucket}/domains/managed", async (string bucket, PublicAccessRequest request, IStorageService service) =>
            ManagedView.From(await service.SetPublicAccessAsync(bucket, request.Enabled)));

        buckets.MapPost("/{bucket}/domains/custom", async (string bucket, CustomDomainRequest request, IStorageService service) =>
            CustomDomainView.From(await service.AddCustomDomainAsync(bucket, request.Domain ?? string.Empty, request.Enabled ?? true, request.MinTls)));

        buckets.MapPut("/{bucket}/domains/custom/{domain}", async (string bucket, string domain, CustomDomainUpdate request, IStorageService service) =>
            CustomDomainView.From(await service.UpdateCustomDomainAsync(bucket, domain, request.Enabled ?? true, request.MinTls)));

        buckets.MapDelete("/{bucket}/domains/custom/{domain}", async (string bucket, string domain, IStorageService service) =>
        {
            await service.RemoveCustomDomainAsync(bucket, domain);
            return Results.NoContent();
        });
    }

    // ── contracts ────────────────────────────────────────────────

    public sealed record OptionsView(IReadOnlyList<string> LocationHints, IReadOnlyList<string> StorageClasses, IReadOnlyList<string> MinTlsVersions);

    public sealed record CreateBucketRequest(string? Name, string? LocationHint, string? StorageClass);

    public sealed record PublicAccessRequest(bool Enabled);

    public sealed record CustomDomainRequest(string? Domain, bool? Enabled, string? MinTls);

    public sealed record CustomDomainUpdate(bool? Enabled, string? MinTls);

    public sealed record BucketsResponse(IReadOnlyList<BucketView> Buckets);

    public sealed record BucketView(string Name, DateTime? CreatedAt, string? Location, string? StorageClass, string? Jurisdiction)
    {
        public static BucketView From(R2Bucket b) => new(b.Name, b.CreatedAt, b.Location, b.StorageClass, b.Jurisdiction);
    }

    public sealed record ManagedView(string Domain, bool Enabled)
    {
        public static ManagedView From(R2ManagedDomain m) => new(m.Domain, m.Enabled);
    }

    public sealed record CustomDomainView(
        string Domain, bool Enabled, string? ZoneId, string? ZoneName, string? MinTls, string? OwnershipStatus, string? SslStatus)
    {
        public static CustomDomainView From(R2CustomDomain d) =>
            new(d.Domain, d.Enabled, d.ZoneId, d.ZoneName, d.MinTls, d.OwnershipStatus, d.SslStatus);
    }

    public sealed record DomainsView(ManagedView Managed, IReadOnlyList<CustomDomainView> Custom, IReadOnlyList<CloudflareZone> Zones)
    {
        public static DomainsView From(BucketDomains d) =>
            new(ManagedView.From(d.Managed), d.Custom.Select(CustomDomainView.From).ToList(), d.Zones);
    }
}
