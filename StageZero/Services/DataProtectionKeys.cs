namespace StageZero.Services;

/// <summary>
/// Where the Data Protection key ring lives: <c>dp-keys/</c> in the data directory, on the
/// mounted volume in a container. Auth cookies, antiforgery tokens, the TOTP secrets and the
/// stored Cloudflare API tokens are all encrypted with it, so it must survive restarts.
/// </summary>
public static class DataProtectionKeys
{
    public const string DirectoryName = "dp-keys";

    /// <summary>
    /// A second, unused ring directory from a period when Program.cs configured Data
    /// Protection twice. The later call won, so every install wrote to <c>dp-keys/</c>; any
    /// key files here are copied across anyway — the ring reads every key file in its
    /// directory — so nothing ever encrypted under this one becomes unreadable.
    /// </summary>
    public const string LegacyDirectoryName = "keys";

    public static DirectoryInfo Prepare(string appDataDirectory)
    {
        var ring = Directory.CreateDirectory(Path.Combine(appDataDirectory, DirectoryName));

        var legacy = new DirectoryInfo(Path.Combine(appDataDirectory, LegacyDirectoryName));
        if (legacy.Exists)
        {
            foreach (var key in legacy.EnumerateFiles("key-*.xml"))
            {
                var target = Path.Combine(ring.FullName, key.Name);
                if (!File.Exists(target))
                {
                    key.CopyTo(target);
                }
            }
        }

        return ring;
    }
}
