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
