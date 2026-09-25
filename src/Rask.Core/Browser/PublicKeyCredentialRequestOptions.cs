using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>Options for authenticating with a passkey — <c>navigator.credentials.get</c>.</summary>
public sealed record PublicKeyCredentialRequestOptions
{
    /// <summary>Server-issued random challenge as base64url.</summary>
    public required string Challenge { get; init; }

    /// <summary>Ceremony timeout in milliseconds.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeoutMs { get; init; }

    /// <summary>The RP id (defaults to the origin's domain when omitted).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RpId { get; init; }

    /// <summary>Which credentials may be used; omit for a discoverable-credential (usernameless) flow.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CredentialDescriptor>? AllowCredentials { get; init; }

    /// <summary>User-verification requirement — <c>"required"</c>/<c>"preferred"</c>/<c>"discouraged"</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserVerification { get; init; }
}
