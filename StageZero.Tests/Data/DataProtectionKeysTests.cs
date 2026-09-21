using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StageZero.Services;
using StageZero.Tests.Infrastructure;

namespace StageZero.Tests.Data;

public sealed class DataProtectionKeysTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("stagezero-keys-").FullName;

    [Fact]
    public void A_secret_protected_under_the_legacy_ring_still_opens()
    {
        var legacy = Directory.CreateDirectory(Path.Combine(_home, DataProtectionKeys.LegacyDirectoryName));
        var protectedToken = Provider(legacy).CreateProtector("StageZero.Tunnel").Protect("cloudflare-token");

        var ring = DataProtectionKeys.Prepare(_home);

        Assert.Equal("cloudflare-token", Provider(ring).CreateProtector("StageZero.Tunnel").Unprotect(protectedToken));
    }

    [Fact]
    public void A_secret_protected_under_the_current_ring_still_opens()
    {
        var ring = DataProtectionKeys.Prepare(_home);
        var protectedToken = Provider(ring).CreateProtector("StageZero.Tunnel").Protect("cloudflare-token");

        var again = DataProtectionKeys.Prepare(_home);

        Assert.Equal("cloudflare-token", Provider(again).CreateProtector("StageZero.Tunnel").Unprotect(protectedToken));
    }

    private static IDataProtectionProvider Provider(DirectoryInfo keys) =>
        DataProtectionProvider.Create(keys, options => options.SetApplicationName("StageZero"));

    public void Dispose() => Directory.Delete(_home, recursive: true);
}

[Collection(AppCollection.Name)]
public class DataProtectionConfigurationTests
{
    [Fact]
    public async Task The_app_keeps_exactly_one_key_ring_in_the_data_directory()
    {
        await using var app = new StageZeroApp();

        var options = app.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        var repository = Assert.IsType<FileSystemXmlRepository>(options.XmlRepository);
        Assert.Equal(Path.Combine(app.DataDirectory, DataProtectionKeys.DirectoryName), repository.Directory.FullName);
        Assert.False(Directory.Exists(Path.Combine(app.DataDirectory, DataProtectionKeys.LegacyDirectoryName)));
    }
}
