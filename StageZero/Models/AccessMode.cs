namespace StageZero.Models;

/// <summary>
/// How Cloudflare Access protects a tunnel route's hostname.
/// </summary>
public enum AccessMode
{
    /// <summary>No Access application. The hostname is reachable by anyone. Must be chosen explicitly.</summary>
    None,

    /// <summary>A single allow policy matching the configured emails and/or email domains.</summary>
    Identity,

    /// <summary>A single non-identity policy matching one service token. Machine-to-machine only.</summary>
    ServiceToken,

    /// <summary>Both policies, so a human or a service token can reach the hostname.</summary>
    Both
}

/// <summary>
/// Converts between <see cref="AccessMode"/> and the snake_case strings used in the
/// database and the UI, so the persisted value stays readable and stable.
/// </summary>
public static class AccessModes
{
    public const string NoneValue = "none";
    public const string IdentityValue = "identity";
    public const string ServiceTokenValue = "service_token";
    public const string BothValue = "both";

    public static string ToWireValue(this AccessMode mode) => mode switch
    {
        AccessMode.None => NoneValue,
        AccessMode.Identity => IdentityValue,
        AccessMode.ServiceToken => ServiceTokenValue,
        AccessMode.Both => BothValue,
        _ => NoneValue
    };

    /// <summary>Returns false for anything that is not one of the four documented modes.</summary>
    public static bool TryParse(string? value, out AccessMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case NoneValue:
                mode = AccessMode.None;
                return true;
            case IdentityValue:
                mode = AccessMode.Identity;
                return true;
            case ServiceTokenValue:
                mode = AccessMode.ServiceToken;
                return true;
            case BothValue:
                mode = AccessMode.Both;
                return true;
            default:
                mode = AccessMode.None;
                return false;
        }
    }

    /// <summary>
    /// Parses a stored value, falling back to <see cref="AccessMode.None"/>. Used where an
    /// out parameter is not available, such as EF Core value conversions.
    /// </summary>
    public static AccessMode Parse(string? value) => TryParse(value, out var mode) ? mode : AccessMode.None;

    /// <summary>True when the mode needs an identity allow policy.</summary>
    public static bool RequiresIdentityPolicy(this AccessMode mode) =>
        mode is AccessMode.Identity or AccessMode.Both;

    /// <summary>True when the mode needs a service token policy.</summary>
    public static bool RequiresServiceTokenPolicy(this AccessMode mode) =>
        mode is AccessMode.ServiceToken or AccessMode.Both;

    /// <summary>True when the mode needs an Access application at all.</summary>
    public static bool RequiresApplication(this AccessMode mode) =>
        mode != AccessMode.None;

    /// <summary>Human-readable label for the UI.</summary>
    public static string ToDisplayName(this AccessMode mode) => mode switch
    {
        AccessMode.None => "None (public)",
        AccessMode.Identity => "Identity",
        AccessMode.ServiceToken => "Service token",
        AccessMode.Both => "Identity + service token",
        _ => "None (public)"
    };
}
