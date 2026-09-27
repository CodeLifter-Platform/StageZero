using System.Text.RegularExpressions;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests;

/// <summary>
/// A self-hosted page loads nothing from anyone else: no CDN, font service or script kit.
/// Each would learn about every visit and break rendering when offline.
/// </summary>
[Collection(AppCollection.Name)]
public partial class SelfContainedAssetsTests
{
    [Theory]
    [InlineData("/setup")]
    [InlineData("/Error")]
    public async Task A_page_references_no_outside_asset(string path)
    {
        await using var app = new StageZeroApp();
        using var client = app.CreateNonRedirectingClient();

        var html = await client.GetStringAsync(path);

        Assert.Empty(ExternalAsset().Matches(html).Select(m => m.Value));
    }

    [GeneratedRegex("""<(?:link|script|img|iframe)\b[^>]*\b(?:href|src)\s*=\s*["'](?:https?:)?//[^"']+""", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalAsset();
}
