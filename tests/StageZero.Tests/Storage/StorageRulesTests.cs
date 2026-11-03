using StageZero.Services.Dns;
using StageZero.Services.Storage;

namespace StageZero.Tests.Storage;

/// <summary>The rules StorageService applies before anything reaches Cloudflare.</summary>
public class StorageRulesTests
{
    [Theory]
    [InlineData("abc", null)]
    [InlineData("my-bucket-01", null)]
    [InlineData("", "required")]
    [InlineData("ab", "3 to 63")]
    [InlineData("Bucket", "lowercase")]
    [InlineData("-bucket", "lowercase")]
    [InlineData("bucket-", "lowercase")]
    [InlineData("my_bucket", "lowercase")]
    [InlineData("my.bucket", "lowercase")]
    public void Bucket_names_follow_cloudflares_rule(string name, string? problemContains)
    {
        var problem = StorageService.BucketNameProblem(name);

        if (problemContains is null) Assert.Null(problem);
        else Assert.Contains(problemContains, problem);
    }

    [Fact]
    public void A_name_of_sixty_three_characters_passes_and_sixty_four_does_not()
    {
        Assert.Null(StorageService.BucketNameProblem(new string('a', 63)));
        Assert.Contains("3 to 63", StorageService.BucketNameProblem(new string('a', 64)));
    }

    [Theory]
    [InlineData("files.example.com", null)]
    [InlineData("a.b", null)]
    [InlineData("", "required")]
    [InlineData("example", "full hostname")]
    [InlineData("-bad.example.com", "full hostname")]
    [InlineData("bad_host.example.com", "full hostname")]
    [InlineData("http://files.example.com", "full hostname")]
    public void A_custom_domain_is_a_full_hostname(string domain, string? problemContains)
    {
        var problem = StorageService.DomainProblem(domain);

        if (problemContains is null) Assert.Null(problem);
        else Assert.Contains(problemContains, problem);
    }

    [Fact]
    public void The_zone_for_a_hostname_is_the_most_specific_one_the_token_sees()
    {
        var zones = new List<CloudflareZone>
        {
            new() { Id = "z1", Name = "example.com" },
            new() { Id = "z2", Name = "files.example.com" },
            new() { Id = "z3", Name = "example.org" },
        };

        Assert.Equal("z2", StorageService.ZoneFor("cdn.files.example.com", zones)?.Id);
        Assert.Equal("z1", StorageService.ZoneFor("www.example.com", zones)?.Id);
        Assert.Equal("z1", StorageService.ZoneFor("Example.COM.", zones)?.Id); // the apex itself, any case, trailing dot
        Assert.Null(StorageService.ZoneFor("notexample.com", zones)); // a suffix match needs the dot
        Assert.Null(StorageService.ZoneFor("cdn.other.net", zones));
    }
}

/// <summary>The rules for objects and per-app credentials, and how S3 credentials come out of a token.</summary>
public class StorageObjectRulesTests
{
    [Theory]
    [InlineData("a.txt", null)]
    [InlineData("photos/2026/a b.jpg", null)]
    [InlineData("", "required")]
    [InlineData("photos/", "names a file")]
    [InlineData("/a.txt", "start with a slash")]
    [InlineData("a/../b.txt", ". or ..")]
    [InlineData("./a.txt", ". or ..")]
    [InlineData("a//b.txt", "empty folder name")]
    [InlineData("a\nb", "control characters")]
    public void Object_keys_name_a_file_inside_the_bucket(string key, string? problemContains)
    {
        var problem = StorageService.ObjectKeyProblem(key);

        if (problemContains is null) Assert.Null(problem);
        else Assert.Contains(problemContains, problem);
    }

    [Fact]
    public void A_key_of_1024_bytes_passes_and_1025_does_not()
    {
        Assert.Null(StorageService.ObjectKeyProblem(new string('k', 1024)));
        Assert.Contains("1024 bytes", StorageService.ObjectKeyProblem(new string('k', 1025)));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("photos/", null)]
    [InlineData("photos/2026/", null)]
    [InlineData("photos", "ends with a slash")]
    [InlineData("../", ". or ..")]
    public void Prefixes_are_folders(string? prefix, string? problemContains)
    {
        var problem = StorageService.PrefixProblem(prefix);

        if (problemContains is null) Assert.Null(problem);
        else Assert.Contains(problemContains, problem);
    }

    [Fact]
    public void Credential_names_are_cloudflares_120_characters()
    {
        Assert.Null(StorageService.CredentialNameProblem("Specter"));
        Assert.Null(StorageService.CredentialNameProblem(new string('n', 120)));
        Assert.Contains("required", StorageService.CredentialNameProblem("  "));
        Assert.Contains("120", StorageService.CredentialNameProblem(new string('n', 121)));
    }

    [Fact]
    public void The_s3_secret_is_the_sha256_of_the_token_and_the_access_key_is_its_id()
    {
        // The SHA-256 test vector for "abc".
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", R2S3Credentials.SecretFor("abc"));

        var credentials = R2S3Credentials.FromToken("acct", "token-id", "abc", null);

        Assert.Equal(("token-id", "https://acct.r2.cloudflarestorage.com"), (credentials.AccessKeyId, credentials.Endpoint));
    }

    [Theory]
    [InlineData(null, "https://acct.r2.cloudflarestorage.com")]
    [InlineData("default", "https://acct.r2.cloudflarestorage.com")]
    [InlineData("eu", "https://acct.eu.r2.cloudflarestorage.com")]
    [InlineData("FedRAMP", "https://acct.fedramp.r2.cloudflarestorage.com")]
    public void Jurisdiction_buckets_have_their_own_endpoint(string? jurisdiction, string endpoint)
    {
        Assert.Equal(endpoint, R2S3Credentials.EndpointFor("acct", jurisdiction));
    }

    [Fact]
    public void A_bucket_policy_resource_round_trips_through_cloudflares_key_format()
    {
        Assert.Equal("com.cloudflare.edge.r2.bucket.acct_default_media", StorageService.BucketResource("acct", null, "media"));
        Assert.Equal("com.cloudflare.edge.r2.bucket.acct_eu_docs", StorageService.BucketResource("acct", "EU", "docs"));
        Assert.Equal("media", StorageService.BucketFromResource("acct", "com.cloudflare.edge.r2.bucket.acct_default_media"));
        Assert.Equal("my_bucket", StorageService.BucketFromResource("acct", "com.cloudflare.edge.r2.bucket.acct_default_my_bucket")); // the name may hold underscores
        Assert.Null(StorageService.BucketFromResource("acct", "com.cloudflare.edge.r2.bucket.other_default_media"));
        Assert.Null(StorageService.BucketFromResource("acct", "com.cloudflare.api.account.acct"));
    }
}
