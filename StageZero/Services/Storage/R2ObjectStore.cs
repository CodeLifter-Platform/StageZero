using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace StageZero.Services.Storage;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The objects in an R2 bucket, over R2's S3-compatible API. One folder at a time: a prefix
/// and the keys and sub-prefixes directly under it. Credentials are derived from an API
/// token (see <see cref="R2S3Credentials.FromToken"/>) and never stored on their own.
/// </summary>
public interface IR2ObjectStore
{
    /// <summary>One "folder": the sub-prefixes and the objects directly under <paramref name="prefix"/>.</summary>
    Task<R2ObjectListing> ListAsync(R2S3Credentials credentials, string bucket, string prefix, string? cursor);

    /// <summary>Every key under <paramref name="prefix"/>, however deep. For deleting a folder.</summary>
    Task<List<string>> ListKeysAsync(R2S3Credentials credentials, string bucket, string prefix);

    Task PutAsync(R2S3Credentials credentials, string bucket, string key, Stream body, long length, string? contentType);

    /// <summary>Deletes up to 1000 keys. Missing keys are not an error.</summary>
    Task DeleteAsync(R2S3Credentials credentials, string bucket, IReadOnlyList<string> keys);

    /// <summary>A URL that reads one object without credentials, for <paramref name="lifetime"/>.</summary>
    Task<string> PresignGetAsync(R2S3Credentials credentials, string bucket, string key, string fileName, TimeSpan lifetime);
}

// ═══════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// S3 credentials for one R2 endpoint. Cloudflare's rule: the access key is the API token's id,
/// the secret is the SHA-256 of the token's value. Any token with R2 permissions works this way,
/// so the one saved on Settings reaches objects too.
/// </summary>
public sealed record R2S3Credentials(string AccountId, string AccessKeyId, string SecretAccessKey, string? Jurisdiction)
{
    public string Endpoint => EndpointFor(AccountId, Jurisdiction);

    public static R2S3Credentials FromToken(string accountId, string tokenId, string tokenValue, string? jurisdiction) =>
        new(accountId, tokenId, SecretFor(tokenValue), jurisdiction);

    /// <summary>Lowercase hex SHA-256 of the token value: the secret access key Cloudflare expects.</summary>
    public static string SecretFor(string tokenValue) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenValue))).ToLowerInvariant();

    /// <summary>The S3 endpoint: jurisdiction buckets (eu, fedramp) have their own host.</summary>
    public static string EndpointFor(string accountId, string? jurisdiction)
    {
        var j = jurisdiction?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(j) || j == "default"
            ? $"https://{accountId}.r2.cloudflarestorage.com"
            : $"https://{accountId}.{j}.r2.cloudflarestorage.com";
    }
}

public sealed class R2ObjectEntry
{
    /// <summary>The name within the folder; folders end with "/".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The full key (for files) or prefix (for folders).</summary>
    public string Key { get; set; } = string.Empty;

    public bool IsFolder { get; set; }
    public long? Size { get; set; }
    public DateTime? Modified { get; set; }
}

public sealed class R2ObjectListing
{
    public string Prefix { get; set; } = string.Empty;
    public List<R2ObjectEntry> Entries { get; set; } = new();

    /// <summary>More under this prefix than one page holds; pass this back to continue.</summary>
    public string? Cursor { get; set; }
}

public class R2ObjectStoreException : Exception
{
    public R2ObjectStoreException(string message) : base(message) { }
    public R2ObjectStoreException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class R2ObjectStore : IR2ObjectStore
{
    private const int PAGE = 1000;

    private readonly ILogger<R2ObjectStore> _logger;

    public R2ObjectStore(ILogger<R2ObjectStore> logger)
    {
        _logger = logger;
    }

    public async Task<R2ObjectListing> ListAsync(R2S3Credentials credentials, string bucket, string prefix, string? cursor)
    {
        using var s3 = Client(credentials);
        var response = await Call(() => s3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = bucket, Prefix = prefix, Delimiter = "/", MaxKeys = PAGE, ContinuationToken = cursor
        }), $"list {Describe(bucket, prefix)}");

        var listing = new R2ObjectListing { Prefix = prefix };
        foreach (var sub in response.CommonPrefixes ?? new List<string>())
        {
            listing.Entries.Add(new R2ObjectEntry { Name = sub[prefix.Length..], Key = sub, IsFolder = true });
        }

        foreach (var o in response.S3Objects ?? new List<S3Object>())
        {
            if (o.Key == prefix) continue; // the folder's own marker object
            listing.Entries.Add(new R2ObjectEntry { Name = o.Key[prefix.Length..], Key = o.Key, Size = o.Size, Modified = o.LastModified?.ToUniversalTime() });
        }

        listing.Cursor = response.IsTruncated == true ? response.NextContinuationToken : null;
        return listing;
    }

    public async Task<List<string>> ListKeysAsync(R2S3Credentials credentials, string bucket, string prefix)
    {
        using var s3 = Client(credentials);
        var keys = new List<string>();
        string? cursor = null;
        do
        {
            var response = await Call(() => s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket, Prefix = prefix, MaxKeys = PAGE, ContinuationToken = cursor
            }), $"list everything under {Describe(bucket, prefix)}");
            keys.AddRange((response.S3Objects ?? new List<S3Object>()).Select(o => o.Key));
            cursor = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (cursor is not null);

        return keys;
    }

    public async Task PutAsync(R2S3Credentials credentials, string bucket, string key, Stream body, long length, string? contentType)
    {
        using var s3 = Client(credentials);
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = body,
            AutoCloseStream = false,
            UseChunkEncoding = false,
            DisablePayloadSigning = true, // the body is streamed once, over TLS; R2 takes UNSIGNED-PAYLOAD
        };
        request.Headers.ContentLength = length;
        if (!string.IsNullOrWhiteSpace(contentType)) request.ContentType = contentType;
        await Call(() => s3.PutObjectAsync(request), $"write {Describe(bucket, key)}");
    }

    public async Task DeleteAsync(R2S3Credentials credentials, string bucket, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return;
        using var s3 = Client(credentials);
        var response = await Call(() => s3.DeleteObjectsAsync(new DeleteObjectsRequest
        {
            BucketName = bucket, Objects = keys.Select(k => new KeyVersion { Key = k }).ToList(), Quiet = true
        }), $"delete {keys.Count} object(s) from {bucket}");

        if (response.DeleteErrors is { Count: > 0 } errors)
        {
            throw new R2ObjectStoreException($"R2 kept {errors.Count} of {keys.Count} object(s): {errors[0].Key} — {errors[0].Message}");
        }
    }

    public async Task<string> PresignGetAsync(R2S3Credentials credentials, string bucket, string key, string fileName, TimeSpan lifetime)
    {
        using var s3 = Client(credentials);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket, Key = key, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.Add(lifetime)
        };
        request.ResponseHeaderOverrides.ContentDisposition = $"attachment; filename=\"{fileName.Replace("\"", string.Empty)}\"";
        return await Call(() => s3.GetPreSignedURLAsync(request), $"sign a download of {Describe(bucket, key)}");
    }

    // ── helpers ──────────────────────────────────────────────────

    private static AmazonS3Client Client(R2S3Credentials credentials) =>
        new(new BasicAWSCredentials(credentials.AccessKeyId, credentials.SecretAccessKey), new AmazonS3Config
        {
            ServiceURL = credentials.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });

    private static string Describe(string bucket, string key) => key.Length == 0 ? bucket : $"{bucket}/{key}";

    private async Task<T> Call<T>(Func<Task<T>> call, string operation)
    {
        try
        {
            return await call();
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(ex, "R2 S3 API failed to {Operation}: {Code}", operation, ex.ErrorCode);
            var detail = string.IsNullOrWhiteSpace(ex.ErrorCode) ? ex.Message : $"{ex.ErrorCode} — {ex.Message}";
            throw new R2ObjectStoreException($"R2 could not {operation}: {detail}", ex);
        }
        catch (AmazonServiceException ex)
        {
            _logger.LogWarning(ex, "R2 S3 API failed to {Operation}", operation);
            throw new R2ObjectStoreException($"R2 could not {operation}: {ex.Message}", ex);
        }
    }
}
