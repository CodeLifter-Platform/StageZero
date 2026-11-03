using Microsoft.AspNetCore.Http.Features;
using StageZero.Services.Dns;
using StageZero.Services.Storage;

namespace StageZero.Api;

/// <summary>
/// R2 buckets, the hostnames in front of them, their objects, and per-app credentials. The
/// saved token stays on the server (objects use S3 credentials derived from it there); a
/// minted credential's secrets are in exactly one response, on create or roll.
/// </summary>
public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this IEndpointRouteBuilder api)
    {
        var storage = api.MapGroup("/storage").RequireAuthorization(Session.OperatorPolicy).WithUserFacingErrors();

        storage.MapGet("/options", () => new OptionsView(StorageService.LocationHints, StorageService.StorageClasses, StorageService.MinTlsVersions, StorageService.AccessLevels));

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

        // ── objects ──
        buckets.MapGet("/{bucket}/objects", async (string bucket, string? prefix, string? cursor, IStorageService service) =>
            ObjectsView.From(await service.ListObjectsAsync(bucket, prefix, cursor)));

        // The file is the request body, streamed straight to R2; the key is the rest of the path.
        buckets.MapPut("/{bucket}/objects/{**key}", async (string bucket, string key, HttpRequest request, IStorageService service) =>
        {
            request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize = null;
            var entry = await service.UploadObjectAsync(bucket, key, request.Body, request.ContentLength ?? -1, request.ContentType);
            return Results.Created($"/api/storage/buckets/{Uri.EscapeDataString(bucket)}/objects/{entry.Key}", ObjectView.From(entry));
        });

        buckets.MapPost("/{bucket}/folders", async (string bucket, CreateFolderRequest request, IStorageService service) =>
        {
            var entry = await service.CreateFolderAsync(bucket, request.Prefix ?? string.Empty);
            return Results.Created($"/api/storage/buckets/{Uri.EscapeDataString(bucket)}/objects?prefix={Uri.EscapeDataString(entry.Key)}", ObjectView.From(entry));
        });

        buckets.MapPost("/{bucket}/objects/delete", async (string bucket, DeleteObjectsRequest request, IStorageService service) =>
            new DeletedView(await service.DeleteObjectsAsync(bucket, request.Keys ?? [], request.Folders ?? [])));

        buckets.MapGet("/{bucket}/download", async (string bucket, string? key, IStorageService service) =>
        {
            var (url, expiresAt) = await service.DownloadUrlAsync(bucket, key ?? string.Empty);
            return new DownloadView(url, expiresAt);
        });

        // ── per-app credentials ──
        var credentials = storage.MapGroup("/credentials");

        credentials.MapGet("/", async (IStorageService service) =>
            new CredentialsResponse((await service.GetAppCredentialsAsync()).Select(CredentialView.From).ToList()));

        credentials.MapPost("/", async (CreateCredentialRequest request, IStorageService service) =>
        {
            var created = await service.CreateAppCredentialAsync(request.Name ?? string.Empty, request.Buckets ?? [], request.Access ?? string.Empty, request.ExpiresInDays);
            return Results.Created($"/api/storage/credentials/{created.Credential.Id}", CreatedCredentialView.From(created));
        });

        credentials.MapPost("/{id}/roll", async (string id, IStorageService service) =>
            CreatedCredentialView.From(await service.RollAppCredentialAsync(id)));

        credentials.MapDelete("/{id}", async (string id, IStorageService service) =>
        {
            await service.DeleteAppCredentialAsync(id);
            return Results.NoContent();
        });
    }

    // ── contracts ────────────────────────────────────────────────

    public sealed record OptionsView(IReadOnlyList<string> LocationHints, IReadOnlyList<string> StorageClasses, IReadOnlyList<string> MinTlsVersions, IReadOnlyList<string> AccessLevels);

    public sealed record CreateFolderRequest(string? Prefix);

    public sealed record DeleteObjectsRequest(IReadOnlyList<string>? Keys, IReadOnlyList<string>? Folders);

    public sealed record DeletedView(int Deleted);

    public sealed record DownloadView(string Url, DateTime ExpiresAt);

    public sealed record CreateCredentialRequest(string? Name, IReadOnlyList<string>? Buckets, string? Access, int? ExpiresInDays);

    public sealed record ObjectView(string Name, string Key, bool IsFolder, long? Size, DateTime? Modified)
    {
        public static ObjectView From(R2ObjectEntry e) => new(e.Name, e.Key, e.IsFolder, e.Size, e.Modified);
    }

    public sealed record ObjectsView(string Prefix, IReadOnlyList<ObjectView> Entries, string? Cursor)
    {
        public static ObjectsView From(R2ObjectListing l) => new(l.Prefix, l.Entries.Select(ObjectView.From).ToList(), l.Cursor);
    }

    public sealed record CredentialsResponse(IReadOnlyList<CredentialView> Credentials);

    public sealed record CredentialView(
        string Id, string Name, string Status, string Access, IReadOnlyList<AppCredentialBucket> Buckets, DateTime? IssuedOn, DateTime? ExpiresOn, DateTime? LastUsedOn)
    {
        public static CredentialView From(AppCredential c) => new(c.Id, c.Name, c.Status, c.Access, c.Buckets, c.IssuedOn, c.ExpiresOn, c.LastUsedOn);
    }

    /// <summary>The one response that carries secrets. The web app shows them once and keeps nothing.</summary>
    public sealed record CreatedCredentialView(CredentialView Credential, string Token, string AccessKeyId, string SecretAccessKey)
    {
        public static CreatedCredentialView From(CreatedAppCredential c) => new(CredentialView.From(c.Credential), c.Token, c.AccessKeyId, c.SecretAccessKey);
    }

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
