using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>Options for creating (registering) a passkey — <c>navigator.credentials.create</c>.</summary>
public sealed record PublicKeyCredentialCreationOptions
{
    /// <summary>Server-issued random challenge as base64url (sign it, don't reuse).</summary>
    public required string Challenge { get; init; }

    /// <summary>The relying party (your site).</summary>
    public required RelyingParty Rp { get; init; }

    /// <summary>The user account.</summary>
    public required PublicKeyCredentialUser User { get; init; }

    /// <summary>Accepted algorithms; defaults to ES256 + RS256 when omitted.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PubKeyCredParam>? PubKeyCredParams { get; init; }

    /// <summary>Ceremony timeout in milliseconds.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeoutMs { get; init; }

    /// <summary>Attestation conveyance — <c>"none"</c> (default) / <c>"indirect"</c> / <c>"direct"</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Attestation { get; init; }

    /// <summary>Authenticator preferences.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthenticatorSelection? AuthenticatorSelection { get; init; }

    /// <summary>Credentials already registered for this user, to avoid duplicates.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CredentialDescriptor>? ExcludeCredentials { get; init; }
}
