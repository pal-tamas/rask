using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>Authenticator preferences for a registration ceremony.</summary>
public sealed record AuthenticatorSelection
{
    /// <summary><c>"platform"</c> (built-in biometric) or <c>"cross-platform"</c> (roaming security key).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthenticatorAttachment { get; init; }

    /// <summary>Whether to create a discoverable (resident) credential — <c>"required"</c>/<c>"preferred"</c>/<c>"discouraged"</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResidentKey { get; init; }

    /// <summary>User-verification requirement — <c>"required"</c>/<c>"preferred"</c>/<c>"discouraged"</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserVerification { get; init; }
}
