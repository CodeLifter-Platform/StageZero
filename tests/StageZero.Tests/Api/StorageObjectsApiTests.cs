using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using StageZero.Api;
using StageZero.Services.Storage;
using StageZero.Tests.Dns;
using StageZero.Tests.Fakes;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Api;

/// <summary>
/// Objects in a bucket and per-app credentials over HTTP: S3 credentials derived from the saved
/// token on the server, uploads streamed through, and a minted credential's secrets in exactly
/// one response. R2's S3 API and Cloudflare's tokens API are in-memory fakes.
/// </summary>
[Collection(AppCollection.Name)]
public class StorageObjectsApiTests
{
    private const string AccountId = "account-1";
    private const string OwnTokenId = "token-stagezero";
    private const string Url = "/api/storage/buckets";

    private readonly FakeCloudflareR2Service _r2 = new();
    private readonly FakeR2ObjectStore _objects = new();
    private readonly FakeCloudflareAccountTokenService _tokens = new();

    private StageZeroApp CreateApp(string? tokenId = OwnTokenId)
    {
        var app = new StageZeroApp
        {
            ConfigureServices = services =>
            {
                services.AddScoped<ICloudflareR2Service>(_ => _r2);
                services.AddScoped<IR2ObjectStore>(_ => _objects);
                services.AddScoped<ICloudflareAccountTokenService>(_ => _tokens);
            }
        };
        app.Http.OnGet("https://api.cloudflare.com/client/v4/zones", CloudflareFakes.List(new { id = "zone-1", name = "example.com" }));
        CloudflareFakes.SaveTokenAsync(app, AccountId, tokenId).GetAwaiter().GetResult();
        _r2.Buckets.Add(new R2Bucket { Name = "media" });
        _r2.Buckets.Add(new R2Bucket { Name = "eu-docs", Jurisdiction = "eu" });
        return app;
    }

    [Fact]
    public async Task A_folder_lists_its_subfolders_first_then_its_files_with_credentials_derived_from_the_saved_token()
    {
        await using var app = CreateApp();
        _objects.Put("media", "photos/2026/a.jpg", "aaa").Put("media", "photos/b.png", "bb").Put("media", "readme.txt", "r").Put("media", "photos/");
        using var client = await app.SignedInClientAsync();

        var root = await (await client.GetAsync($"{Url}/media/objects")).ReadAsync<StorageEndpoints.ObjectsView>();
        var photos = await (await client.GetAsync($"{Url}/media/objects?prefix=photos/")).ReadAsync<StorageEndpoints.ObjectsView>();

        Assert.Equal(["photos/", "readme.txt"], root.Entries.Select(e => e.Name));
        Assert.Equal([true, false], root.Entries.Select(e => e.IsFolder));
        Assert.Equal(["2026/", "b.png"], photos.Entries.Select(e => e.Name)); // the folder's own marker object is not a file
        Assert.Equal(("photos/b.png", 2L), (photos.Entries[1].Key, photos.Entries[1].Size));
        var credentials = Assert.Single(_objects.CredentialsSeen.DistinctBy(c => c.SecretAccessKey));
        Assert.Equal((OwnTokenId, R2S3Credentials.SecretFor(CloudflareFakes.Token), $"https://{AccountId}.r2.cloudflarestorage.com"), (credentials.AccessKeyId, credentials.SecretAccessKey, credentials.Endpoint));
    }

    [Fact]
    public async Task A_jurisdiction_bucket_is_reached_on_its_own_endpoint()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        await (await client.GetAsync($"{Url}/eu-docs/objects")).ReadAsync<StorageEndpoints.ObjectsView>();

        Assert.Equal($"https://{AccountId}.eu.r2.cloudflarestorage.com", Assert.Single(_objects.CredentialsSeen).Endpoint);
    }

    [Fact]
    public async Task An_upload_streams_the_body_to_the_key_with_its_content_type_and_a_folder_is_a_marker_object()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();
        var content = new ByteArrayContent("hello"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var uploaded = await client.PutAsync($"{Url}/media/objects/docs/hello.txt", content);
        var folder = await client.PostJsonAsync($"{Url}/media/folders", new { prefix = "docs/archive/" });
        var listing = await (await client.GetAsync($"{Url}/media/objects?prefix=docs/")).ReadAsync<StorageEndpoints.ObjectsView>();

        var entry = await uploaded.ReadAsync<StorageEndpoints.ObjectView>();
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        Assert.Equal(("hello.txt", "docs/hello.txt", 5L), (entry.Name, entry.Key, entry.Size));
        var stored = _objects.Buckets["media"]["docs/hello.txt"];
        Assert.Equal(("hello", "text/plain"), (System.Text.Encoding.UTF8.GetString(stored.Bytes), stored.ContentType));
        Assert.Equal(HttpStatusCode.Created, folder.StatusCode);
        Assert.Empty(_objects.Buckets["media"]["docs/archive/"].Bytes);
        Assert.Equal(["archive/", "hello.txt"], listing.Entries.Select(e => e.Name));
    }

    [Fact]
    public async Task Keys_that_escape_the_bucket_or_name_a_folder_are_refused_before_r2_is_called()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        // HttpClient collapses ".." in a URL itself, so the rule is reached through a JSON body here.
        var dotDot = await client.PostJsonAsync($"{Url}/media/objects/delete", new { keys = new[] { "a/../b.txt" } });
        var slashed = await client.PutAsync($"{Url}/media/objects/a/b/", new ByteArrayContent([1]));
        var noFolder = await client.PostJsonAsync($"{Url}/media/folders", new { prefix = "no-slash" });
        var missing = await client.GetAsync($"{Url}/nope/objects");

        Assert.Contains(". or ..", await dotDot.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Contains("names a file", await slashed.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Contains("ends with a slash", await noFolder.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Contains("no bucket named nope", await missing.ProblemTitleAsync(HttpStatusCode.NotFound));
        Assert.Empty(_objects.CredentialsSeen);
    }

    [Fact]
    public async Task Deleting_takes_files_by_key_and_folders_with_everything_under_them()
    {
        await using var app = CreateApp();
        _objects.Put("media", "keep.txt").Put("media", "old.txt").Put("media", "tmp/").Put("media", "tmp/x/1.bin").Put("media", "tmp/2.bin");
        using var client = await app.SignedInClientAsync();

        var deleted = await (await client.PostJsonAsync($"{Url}/media/objects/delete", new { keys = new[] { "old.txt" }, folders = new[] { "tmp/" } })).ReadAsync<StorageEndpoints.DeletedView>();
        var nothing = await client.PostJsonAsync($"{Url}/media/objects/delete", new { keys = Array.Empty<string>() });

        Assert.Equal(4, deleted.Deleted);
        Assert.Equal(["keep.txt"], _objects.Buckets["media"].Keys);
        Assert.Contains("Nothing selected", await nothing.ProblemTitleAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task A_download_is_a_signed_link_that_names_the_file_and_expires()
    {
        await using var app = CreateApp();
        _objects.Put("media", "photos/a.jpg", "aaa");
        using var client = await app.SignedInClientAsync();

        var link = await (await client.GetAsync($"{Url}/media/download?key=photos/a.jpg")).ReadAsync<StorageEndpoints.DownloadView>();

        Assert.StartsWith($"https://{AccountId}.r2.cloudflarestorage.com/media/photos/a.jpg?", link.Url);
        Assert.Contains("filename=a.jpg", link.Url);
        Assert.Contains("ttl=600", link.Url);
        Assert.True(link.ExpiresAt > DateTime.UtcNow.AddMinutes(5));
        Assert.DoesNotContain(CloudflareFakes.Token, link.Url);
    }

    [Fact]
    public async Task Objects_need_the_saved_tokens_id_and_r2s_refusal_is_passed_through()
    {
        await using var app = CreateApp(tokenId: null);
        using var client = await app.SignedInClientAsync();

        var noId = await client.GetAsync($"{Url}/media/objects");

        Assert.Contains("Save the token again", await noId.ProblemTitleAsync(HttpStatusCode.Conflict));

        await using var app2 = CreateApp();
        _objects.Refused.Add("media");
        using var client2 = await app2.SignedInClientAsync();

        var refused = await client2.GetAsync($"{Url}/media/objects");

        Assert.Contains("InvalidAccessKeyId", await refused.ProblemTitleAsync(HttpStatusCode.BadGateway));
    }

    [Fact]
    public async Task A_credential_is_minted_for_its_buckets_with_its_secrets_in_that_one_response_and_never_in_the_list()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var created = await client.PostJsonAsync("/api/storage/credentials", new { name = " Specter backups ", buckets = new[] { "media", "eu-docs" }, access = "readwrite", expiresInDays = 90 });
        var listed = await client.GetAsync("/api/storage/credentials");

        var view = await created.ReadAsync<StorageEndpoints.CreatedCredentialView>();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(("token-1", "Specter backups", "readwrite"), (view.Credential.Id, view.Credential.Name, view.Credential.Access));
        Assert.Equal(("cf-token-value-1", "token-1", R2S3Credentials.SecretFor("cf-token-value-1")), (view.Token, view.AccessKeyId, view.SecretAccessKey));
        Assert.Equal([("media", $"https://{AccountId}.r2.cloudflarestorage.com"), ("eu-docs", $"https://{AccountId}.eu.r2.cloudflarestorage.com")],
            view.Credential.Buckets.Select(b => (b.Name, b.Endpoint)));
        Assert.NotNull(view.Credential.ExpiresOn);

        var (name, policies, expiresOn) = Assert.Single(_tokens.Created);
        var policy = Assert.Single(policies);
        Assert.Equal(FakeCloudflareAccountTokenService.BucketWriteGroupId, Assert.Single(policy.Groups).Id);
        Assert.Equal([$"com.cloudflare.edge.r2.bucket.{AccountId}_default_media", $"com.cloudflare.edge.r2.bucket.{AccountId}_eu_eu-docs"], policy.Resources.Keys.Order());
        Assert.True(expiresOn > DateTime.UtcNow.AddDays(89));

        var list = await listed.ReadAsync<StorageEndpoints.CredentialsResponse>();
        Assert.Equal("Specter backups", Assert.Single(list.Credentials).Name);
        var raw = await listed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("cf-token-value-1", raw);
        Assert.DoesNotContain(R2S3Credentials.SecretFor("cf-token-value-1"), raw);
    }

    [Fact]
    public async Task Read_only_credentials_use_the_read_group_and_bad_requests_are_refused_before_cloudflare()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();

        var readOnly = await client.PostJsonAsync("/api/storage/credentials", new { name = "Viewer", buckets = new[] { "media" }, access = "read" });
        var noBucket = await client.PostJsonAsync("/api/storage/credentials", new { name = "Nothing", buckets = Array.Empty<string>(), access = "read" });
        var unknownBucket = await client.PostJsonAsync("/api/storage/credentials", new { name = "Ghost", buckets = new[] { "nope" }, access = "read" });
        var badAccess = await client.PostJsonAsync("/api/storage/credentials", new { name = "Admin", buckets = new[] { "media" }, access = "admin" });
        var noName = await client.PostJsonAsync("/api/storage/credentials", new { name = "", buckets = new[] { "media" }, access = "read" });

        Assert.Equal("read", (await readOnly.ReadAsync<StorageEndpoints.CreatedCredentialView>()).Credential.Access);
        Assert.Equal(FakeCloudflareAccountTokenService.BucketReadGroupId, _tokens.Created.Single().Policies.Single().Groups.Single().Id);
        Assert.Contains("at least one bucket", await noBucket.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Contains("no bucket named nope", await unknownBucket.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Contains("Unknown access level", await badAccess.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Contains("name is required", await noName.ProblemTitleAsync(HttpStatusCode.BadRequest));
        Assert.Single(_tokens.Created);
    }

    [Fact]
    public async Task Rolling_returns_new_secrets_and_deleting_revokes_but_never_stagezeros_own_token()
    {
        await using var app = CreateApp();
        using var client = await app.SignedInClientAsync();
        await client.PostJsonAsync("/api/storage/credentials", new { name = "Reps", buckets = new[] { "media" }, access = "read" });

        var rolled = await (await client.PostJsonAsync("/api/storage/credentials/token-1/roll")).ReadAsync<StorageEndpoints.CreatedCredentialView>();
        var deleted = await client.DeleteAsync("/api/storage/credentials/token-1");
        var own = await client.DeleteAsync($"/api/storage/credentials/{OwnTokenId}");
        var gone = await client.PostJsonAsync("/api/storage/credentials/token-1/roll");

        Assert.Equal(("rolled-value-token-1", "token-1", R2S3Credentials.SecretFor("rolled-value-token-1")), (rolled.Token, rolled.AccessKeyId, rolled.SecretAccessKey));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(["token-1"], _tokens.Deleted);
        Assert.Contains("StageZero itself", await own.ProblemTitleAsync(HttpStatusCode.Conflict));
        Assert.Contains("no credential with id token-1", await gone.ProblemTitleAsync(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Without_the_account_api_tokens_permission_minting_says_which_row_to_add()
    {
        await using var app = CreateApp();
        _tokens.RefuseGroups = true;
        using var client = await app.SignedInClientAsync();

        var response = await client.PostJsonAsync("/api/storage/credentials", new { name = "X", buckets = new[] { "media" }, access = "read" });

        Assert.Contains("Authentication error", await response.ProblemTitleAsync(HttpStatusCode.BadGateway));
    }

    [Fact]
    public async Task Objects_and_credentials_need_a_signed_in_operator()
    {
        await using var app = CreateApp();
        using var anonymous = app.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{Url}/media/objects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/storage/credentials")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsync($"{Url}/media/objects/x", new ByteArrayContent([1]))).StatusCode);
    }
}
