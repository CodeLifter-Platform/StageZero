using StageZero.Services.Storage;

namespace StageZero.Tests.Fakes;

/// <summary>An R2 account's objects in memory, keyed bucket → key. Credentials are recorded, not checked.</summary>
public class FakeR2ObjectStore : IR2ObjectStore
{
    public sealed record StoredObject(byte[] Bytes, string? ContentType, DateTime Modified);

    public Dictionary<string, Dictionary<string, StoredObject>> Buckets { get; } = new();
    public List<R2S3Credentials> CredentialsSeen { get; } = new();
    public List<string> Signed { get; } = new();

    /// <summary>Buckets whose every call fails the way R2 refuses a bad credential.</summary>
    public HashSet<string> Refused { get; } = new();

    public FakeR2ObjectStore Put(string bucket, string key, string content = "", string? contentType = null)
    {
        Objects(bucket)[key] = new StoredObject(System.Text.Encoding.UTF8.GetBytes(content), contentType, new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc));
        return this;
    }

    public Task<R2ObjectListing> ListAsync(R2S3Credentials credentials, string bucket, string prefix, string? cursor)
    {
        Record(credentials, bucket);
        var listing = new R2ObjectListing { Prefix = prefix };
        var folders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (key, stored) in Objects(bucket).Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var rest = key[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash >= 0) { folders.Add(rest[..(slash + 1)]); continue; }
            if (rest.Length == 0) continue;
            listing.Entries.Add(new R2ObjectEntry { Name = rest, Key = key, Size = stored.Bytes.Length, Modified = stored.Modified });
        }

        listing.Entries.InsertRange(0, folders.Select(f => new R2ObjectEntry { Name = f, Key = prefix + f, IsFolder = true }));
        return Task.FromResult(listing);
    }

    public Task<List<string>> ListKeysAsync(R2S3Credentials credentials, string bucket, string prefix)
    {
        Record(credentials, bucket);
        return Task.FromResult(Objects(bucket).Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList());
    }

    public async Task PutAsync(R2S3Credentials credentials, string bucket, string key, Stream body, long length, string? contentType)
    {
        Record(credentials, bucket);
        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer);
        if (buffer.Length != length) throw new R2ObjectStoreException($"R2 could not write {bucket}/{key}: IncompleteBody — {buffer.Length} of {length} bytes arrived");
        Objects(bucket)[key] = new StoredObject(buffer.ToArray(), contentType, new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc));
    }

    public Task DeleteAsync(R2S3Credentials credentials, string bucket, IReadOnlyList<string> keys)
    {
        Record(credentials, bucket);
        foreach (var key in keys) Objects(bucket).Remove(key);
        return Task.CompletedTask;
    }

    public Task<string> PresignGetAsync(R2S3Credentials credentials, string bucket, string key, string fileName, TimeSpan lifetime)
    {
        Record(credentials, bucket);
        Signed.Add($"{bucket}/{key}");
        return Task.FromResult($"{credentials.Endpoint}/{bucket}/{key}?X-Amz-Signature=fake&filename={Uri.EscapeDataString(fileName)}&ttl={(int)lifetime.TotalSeconds}");
    }

    private Dictionary<string, StoredObject> Objects(string bucket)
    {
        if (!Buckets.TryGetValue(bucket, out var objects)) Buckets[bucket] = objects = new Dictionary<string, StoredObject>(StringComparer.Ordinal);
        return objects;
    }

    private void Record(R2S3Credentials credentials, string bucket)
    {
        CredentialsSeen.Add(credentials);
        if (Refused.Contains(bucket)) throw new R2ObjectStoreException($"R2 could not reach {bucket}: InvalidAccessKeyId — The AWS Access Key Id you provided does not exist in our records.");
    }
}
