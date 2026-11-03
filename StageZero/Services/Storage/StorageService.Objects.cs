using System.Text;
using System.Text.RegularExpressions;

namespace StageZero.Services.Storage;

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

/// <summary>An account API token that reaches R2, as the Storage page lists it. Never its value.</summary>
public sealed record AppCredential(
    string Id, string Name, string Status, string Access, IReadOnlyList<AppCredentialBucket> Buckets,
    DateTime? IssuedOn, DateTime? ExpiresOn, DateTime? LastUsedOn);

public sealed record AppCredentialBucket(string Name, string Endpoint);

/// <summary>
/// A credential with its secrets, returned exactly once: on create and on roll. The token
/// value is Cloudflare's; the access key and secret are what an S3 client wants.
/// </summary>
public sealed record CreatedAppCredential(AppCredential Credential, string Token, string AccessKeyId, string SecretAccessKey);

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION — objects and per-app credentials
// ═══════════════════════════════════════════════════════════════

public partial class StorageService
{
    /// <summary>What a per-app credential may do: read objects, or read and write them.</summary>
    public static readonly IReadOnlyList<string> AccessLevels = ["read", "readwrite"];

    /// <summary>How long a download link works. Long enough to click, short enough not to leak.</summary>
    public static readonly TimeSpan DownloadLinkLifetime = TimeSpan.FromMinutes(10);

    private const string BucketItemWrite = "Workers R2 Storage Bucket Item Write";
    private const string BucketItemRead = "Workers R2 Storage Bucket Item Read";
    private const string R2GroupPrefix = "Workers R2 Storage";
    private const string BucketResourcePrefix = "com.cloudflare.edge.r2.bucket.";

    // ── rules ────────────────────────────────────────────────────

    /// <summary>Why a string is unusable as an object key, or null. A key names a file, so it never ends with "/".</summary>
    public static string? ObjectKeyProblem(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "Object key is required.";
        if (key.EndsWith('/')) return "An object key names a file; folders end with a slash and are created separately.";
        return PathProblem(key);
    }

    /// <summary>Why a string is unusable as a folder prefix, or null. Empty is the bucket root; otherwise it ends with "/".</summary>
    public static string? PrefixProblem(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return null;
        if (!prefix.EndsWith('/')) return "A folder prefix ends with a slash.";
        return PathProblem(prefix);
    }

    private static string? PathProblem(string path)
    {
        if (Encoding.UTF8.GetByteCount(path) > 1024) return "A key is at most 1024 bytes.";
        if (path.StartsWith('/')) return "A key does not start with a slash.";
        if (path.Any(c => char.IsControl(c))) return "A key cannot contain control characters.";
        if (path.Split('/').Any(segment => segment is "." or "..")) return "A key cannot contain . or .. segments.";
        if (path.Contains("//", StringComparison.Ordinal)) return "A key cannot contain an empty folder name.";
        return null;
    }

    /// <summary>Why a credential name is unusable, or null. Cloudflare allows 120 characters.</summary>
    public static string? CredentialNameProblem(string? name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) return "Credential name is required.";
        if (name.Length > 120) return "A credential name is at most 120 characters.";
        return null;
    }

    /// <summary>Cloudflare's resource key for one bucket in a token policy.</summary>
    public static string BucketResource(string accountId, string? jurisdiction, string bucket)
    {
        var j = string.IsNullOrWhiteSpace(jurisdiction) ? "default" : jurisdiction.Trim().ToLowerInvariant();
        return $"{BucketResourcePrefix}{accountId}_{j}_{bucket}";
    }

    /// <summary>The bucket a policy resource key names, or null when it is not a bucket key for this account.</summary>
    public static string? BucketFromResource(string accountId, string resource)
    {
        var head = $"{BucketResourcePrefix}{accountId}_";
        if (!resource.StartsWith(head, StringComparison.Ordinal)) return null;
        var rest = resource[head.Length..];
        var underscore = rest.IndexOf('_');
        return underscore < 0 ? null : rest[(underscore + 1)..];
    }

    // ── objects ──────────────────────────────────────────────────

    public async Task<R2ObjectListing> ListObjectsAsync(string bucket, string? prefix, string? cursor)
    {
        bucket = RequireBucketName(bucket);
        prefix = RequirePrefix(prefix);
        var credentials = await RequireS3CredentialsAsync(bucket);
        return await R2(() => _objects.ListAsync(credentials, bucket, prefix, string.IsNullOrEmpty(cursor) ? null : cursor), $"list {Folder(bucket, prefix)}");
    }

    public async Task<R2ObjectEntry> UploadObjectAsync(string bucket, string key, Stream body, long length, string? contentType)
    {
        bucket = RequireBucketName(bucket);
        key = RequireObjectKey(key);
        if (length < 0) throw new UserFacingException("The upload has no Content-Length.");
        var credentials = await RequireS3CredentialsAsync(bucket);

        await R2(() => _objects.PutAsync(credentials, bucket, key, body, length, contentType), $"upload {bucket}/{key}");
        _logger.LogInformation("Uploaded {Bytes} byte(s) to R2 as {Bucket}/{Key}", length, bucket, key);
        return new R2ObjectEntry { Name = key[(key.LastIndexOf('/') + 1)..], Key = key, Size = length, Modified = _time.GetUtcNow().UtcDateTime };
    }

    public async Task<R2ObjectEntry> CreateFolderAsync(string bucket, string prefix)
    {
        bucket = RequireBucketName(bucket);
        if (string.IsNullOrEmpty(prefix)) throw new UserFacingException("Folder name is required.");
        prefix = RequirePrefix(prefix);
        var credentials = await RequireS3CredentialsAsync(bucket);

        // S3 has no folders; a zero-byte object whose key ends in "/" is the convention every browser understands.
        await R2(() => _objects.PutAsync(credentials, bucket, prefix, Stream.Null, 0, null), $"create the folder {bucket}/{prefix}");
        var trimmed = prefix.TrimEnd('/');
        return new R2ObjectEntry { Name = trimmed[(trimmed.LastIndexOf('/') + 1)..] + "/", Key = prefix, IsFolder = true };
    }

    /// <summary>Deletes files by key and folders by prefix (everything under them). Returns how many objects went.</summary>
    public async Task<int> DeleteObjectsAsync(string bucket, IReadOnlyList<string> keys, IReadOnlyList<string> folders)
    {
        bucket = RequireBucketName(bucket);
        var targets = keys.Select(RequireObjectKey).ToList();
        var prefixes = folders.Select(f => string.IsNullOrEmpty(f) ? throw new UserFacingException("A folder to delete needs a name.") : RequirePrefix(f)).ToList();
        if (targets.Count == 0 && prefixes.Count == 0) throw new UserFacingException("Nothing selected to delete.");
        var credentials = await RequireS3CredentialsAsync(bucket);

        foreach (var prefix in prefixes)
        {
            targets.AddRange(await R2(() => _objects.ListKeysAsync(credentials, bucket, prefix), $"list the folder {bucket}/{prefix}"));
        }

        var distinct = targets.Distinct(StringComparer.Ordinal).ToList();
        foreach (var batch in distinct.Chunk(1000))
        {
            await R2(() => _objects.DeleteAsync(credentials, bucket, batch), $"delete {batch.Length} object(s) from {bucket}");
        }

        _logger.LogInformation("Deleted {Count} object(s) from R2 bucket {Bucket}", distinct.Count, bucket);
        return distinct.Count;
    }

    public async Task<(string Url, DateTime ExpiresAt)> DownloadUrlAsync(string bucket, string key)
    {
        bucket = RequireBucketName(bucket);
        key = RequireObjectKey(key);
        var credentials = await RequireS3CredentialsAsync(bucket);
        var fileName = key[(key.LastIndexOf('/') + 1)..];
        var url = await R2(() => _objects.PresignGetAsync(credentials, bucket, key, fileName, DownloadLinkLifetime), $"sign a download of {bucket}/{key}");
        return (url, _time.GetUtcNow().UtcDateTime.Add(DownloadLinkLifetime));
    }

    // ── per-app credentials ──────────────────────────────────────

    public async Task<List<AppCredential>> GetAppCredentialsAsync()
    {
        var (apiToken, accountId) = await RequireCredentialsAsync();
        var buckets = await Cloudflare(() => _r2.ListBucketsAsync(apiToken, accountId), "list the buckets");
        var tokens = await Cloudflare(() => _accountTokens.ListTokensAsync(apiToken, accountId), "list the account's API tokens");
        return tokens
            .Where(t => t.Policies.Any(p => p.PermissionGroups.Any(g => g.StartsWith(R2GroupPrefix, StringComparison.Ordinal))))
            .Select(t => ToAppCredential(t, accountId, buckets))
            .ToList();
    }

    public async Task<CreatedAppCredential> CreateAppCredentialAsync(string name, IReadOnlyList<string> bucketNames, string access, int? expiresInDays)
    {
        var (apiToken, accountId) = await RequireCredentialsAsync();
        name = name?.Trim() ?? string.Empty;
        if (CredentialNameProblem(name) is { } problem) throw new UserFacingException(problem);
        access = Choice(access, AccessLevels, "access level") ?? throw new UserFacingException("Choose read or readwrite access.");
        if (bucketNames.Count == 0) throw new UserFacingException("Choose at least one bucket.");
        if (expiresInDays is <= 0) throw new UserFacingException("Expiry is a number of days from today.");

        var buckets = await Cloudflare(() => _r2.ListBucketsAsync(apiToken, accountId), "list the buckets");
        var chosen = bucketNames.Select(RequireBucketName).Distinct(StringComparer.Ordinal)
            .Select(n => buckets.FirstOrDefault(b => b.Name == n) ?? throw new UserFacingException($"There is no bucket named {n} on this account."))
            .ToList();

        var groups = await Cloudflare(() => _accountTokens.ListPermissionGroupsAsync(apiToken, accountId), "list the permission groups");
        var wanted = access == "readwrite" ? BucketItemWrite : BucketItemRead;
        var group = groups.FirstOrDefault(g => string.Equals(g.Name, wanted, StringComparison.Ordinal))
                    ?? throw UserFacingException.Conflict($"Cloudflare does not offer the permission group \"{wanted}\" to this token. It needs Account → Account API Tokens → Edit.");

        var resources = chosen.ToDictionary(b => BucketResource(accountId, b.Jurisdiction, b.Name), _ => (object)"*", StringComparer.Ordinal);
        var policies = new[] { new CloudflareTokenPolicyRequest([group], resources) };
        var expiresOn = expiresInDays is { } days ? _time.GetUtcNow().UtcDateTime.AddDays(days) : (DateTime?)null;

        var created = await Cloudflare(() => _accountTokens.CreateTokenAsync(apiToken, accountId, name, policies, expiresOn), $"create the credential {name}");
        if (string.IsNullOrEmpty(created.Value))
        {
            throw new UserFacingException($"Cloudflare created {name} but did not return its value. Delete it in the Cloudflare dashboard and try again.");
        }

        _logger.LogInformation("Minted the per-app R2 credential {Name} ({TokenId}) for {Buckets} ({Access})", name, created.Id, string.Join(", ", chosen.Select(b => b.Name)), access);
        return new CreatedAppCredential(ToAppCredential(created, accountId, buckets), created.Value, created.Id, R2S3Credentials.SecretFor(created.Value));
    }

    public async Task DeleteAppCredentialAsync(string tokenId)
    {
        var credentials = await _credentials.RequireAsync(needsAccount: true);
        tokenId = RequireTokenId(tokenId, credentials.TokenId);
        await Cloudflare(() => _accountTokens.DeleteTokenAsync(credentials.ApiToken, credentials.AccountId!, tokenId), $"delete the credential {tokenId}");
    }

    public async Task<CreatedAppCredential> RollAppCredentialAsync(string tokenId)
    {
        var credentials = await _credentials.RequireAsync(needsAccount: true);
        var (apiToken, accountId) = (credentials.ApiToken, credentials.AccountId!);
        tokenId = RequireTokenId(tokenId, credentials.TokenId);

        var tokens = await Cloudflare(() => _accountTokens.ListTokensAsync(apiToken, accountId), "list the account's API tokens");
        var token = tokens.FirstOrDefault(t => t.Id == tokenId) ?? throw UserFacingException.NotFound($"There is no credential with id {tokenId} on this account.");
        var buckets = await Cloudflare(() => _r2.ListBucketsAsync(apiToken, accountId), "list the buckets");

        var value = await Cloudflare(() => _accountTokens.RollTokenAsync(apiToken, accountId, tokenId), $"roll the credential {token.Name}");
        _logger.LogInformation("Rolled the per-app R2 credential {Name} ({TokenId})", token.Name, tokenId);
        return new CreatedAppCredential(ToAppCredential(token, accountId, buckets), value, tokenId, R2S3Credentials.SecretFor(value));
    }

    // ── helpers ──────────────────────────────────────────────────

    private static AppCredential ToAppCredential(CloudflareAccountToken t, string accountId, IReadOnlyList<R2Bucket> buckets)
    {
        var groups = t.Policies.SelectMany(p => p.PermissionGroups).ToList();
        var access = groups.Any(g => g.EndsWith(" Write", StringComparison.Ordinal))
            ? (groups.Any(g => g.StartsWith(R2GroupPrefix + " Bucket", StringComparison.Ordinal)) ? "readwrite" : "admin")
            : "read";
        var names = t.Policies.SelectMany(p => p.Resources)
            .Select(r => BucketFromResource(accountId, r))
            .Where(n => n is not null)
            .Distinct(StringComparer.Ordinal)
            .Select(n => new AppCredentialBucket(n!, R2S3Credentials.EndpointFor(accountId, buckets.FirstOrDefault(b => b.Name == n)?.Jurisdiction)))
            .ToList();
        if (names.Count == 0 && t.Policies.SelectMany(p => p.Resources).Any(r => r.StartsWith("com.cloudflare.api.account.", StringComparison.Ordinal)))
        {
            names.Add(new AppCredentialBucket("*", R2S3Credentials.EndpointFor(accountId, null)));
        }

        return new AppCredential(t.Id, t.Name, t.Status, access, names, t.IssuedOn, t.ExpiresOn, t.LastUsedOn);
    }

    private static string RequireTokenId(string? tokenId, string? ownTokenId)
    {
        tokenId = tokenId?.Trim() ?? string.Empty;
        if (tokenId.Length == 0 || tokenId.Length > 64 || !TokenId().IsMatch(tokenId)) throw new UserFacingException("That is not a credential id.");
        if (string.Equals(tokenId, ownTokenId, StringComparison.Ordinal))
        {
            throw UserFacingException.Conflict("That is the token StageZero itself runs on. Replace or remove it on the Settings page instead.");
        }

        return tokenId;
    }

    private async Task<R2S3Credentials> RequireS3CredentialsAsync(string bucket)
    {
        var credentials = await _credentials.RequireAsync(needsAccount: true);
        if (string.IsNullOrEmpty(credentials.TokenId))
        {
            throw UserFacingException.Conflict(
                "StageZero doesn't know the saved token's id, which R2's S3 API uses as the access key. Save the token again on the Settings page.");
        }

        var buckets = await Cloudflare(() => _r2.ListBucketsAsync(credentials.ApiToken, credentials.AccountId!), "list the buckets");
        var found = buckets.FirstOrDefault(b => b.Name == bucket)
                    ?? throw UserFacingException.NotFound($"There is no bucket named {bucket} on this account.");
        return R2S3Credentials.FromToken(credentials.AccountId!, credentials.TokenId, credentials.ApiToken, found.Jurisdiction);
    }

    private static string RequireObjectKey(string? key) =>
        ObjectKeyProblem(key) is { } problem ? throw new UserFacingException(problem) : key!;

    private static string RequirePrefix(string? prefix) =>
        PrefixProblem(prefix) is { } problem ? throw new UserFacingException(problem) : prefix ?? string.Empty;

    private static string Folder(string bucket, string prefix) => prefix.Length == 0 ? $"the root of {bucket}" : $"{bucket}/{prefix}";

    /// <summary>An R2 S3 call whose failure the operator should read, as a 502 with the detail.</summary>
    private async Task<T> R2<T>(Func<Task<T>> call, string operation)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is not UserFacingException)
        {
            _logger.LogError(ex, "R2 objects: could not {Operation}", operation);
            throw new UserFacingException($"Could not {operation}: {ex.Message}", ex);
        }
    }

    private async Task R2(Func<Task> call, string operation)
    {
        await R2(async () => { await call(); return true; }, operation);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex TokenId();
}
