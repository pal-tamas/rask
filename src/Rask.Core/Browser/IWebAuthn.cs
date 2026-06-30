using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

// All binary fields (challenge, ids, attestation/assertion buffers) cross the interop boundary as
// base64url strings — the framework's __raskWebAuthn helper encodes/decodes the ArrayBuffers at the seam.
// That matches how a relying-party backend exchanges and verifies these values, so the strings can be
// POSTed as-is.

/// <summary>The relying party (your site) for a WebAuthn ceremony.</summary>
/// <param name="Name">Human-readable site name shown in the platform UI.</param>
/// <param name="Id">
///     The RP id — a domain the current origin is a registrable suffix of (defaults to the origin's domain
///     when omitted).
/// </param>
public sealed record RelyingParty(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null);

/// <summary>The user account a passkey is being created for.</summary>
/// <param name="Id">Opaque, stable user handle as base64url (not an email; max 64 bytes decoded).</param>
/// <param name="Name">Account identifier shown in the UI (often an email or username).</param>
/// <param name="DisplayName">Friendly display name.</param>
public sealed record PublicKeyCredentialUser(string Id, string Name, string DisplayName);

/// <summary>A supported public-key algorithm (a <c>pubKeyCredParams</c> entry).</summary>
/// <param name="Alg">COSE algorithm id, e.g. <c>-7</c> (ES256) or <c>-257</c> (RS256).</param>
/// <param name="Type">Credential type — always <c>"public-key"</c>.</param>
public sealed record PubKeyCredParam(int Alg, string Type = "public-key");

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

/// <summary>A reference to an existing credential (to exclude on register, or allow on authenticate).</summary>
/// <param name="Id">The credential id as base64url.</param>
/// <param name="Transports">Optional transport hints, e.g. <c>["internal", "usb"]</c>.</param>
/// <param name="Type">Credential type — always <c>"public-key"</c>.</param>
public sealed record CredentialDescriptor(
    string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Transports = null,
    string Type = "public-key");

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

/// <summary>
///     The result of a registration ceremony (a <c>PublicKeyCredential</c> with an attestation response).
///     POST it to your backend, which verifies the attestation and stores the credential.
/// </summary>
/// <param name="Id">Credential id (base64url).</param>
/// <param name="RawId">Credential raw id (base64url).</param>
/// <param name="ClientDataJson">Client data JSON (base64url) — the backend re-derives and checks it.</param>
/// <param name="AttestationObject">CBOR attestation object (base64url) — holds the new public key.</param>
/// <param name="Transports">Transport hints the authenticator advertised (may be empty/null).</param>
/// <param name="Type">Credential type — <c>"public-key"</c>.</param>
public sealed record AttestationResult(
    string Id,
    string RawId,
    string ClientDataJson,
    string AttestationObject,
    string[]? Transports,
    string Type = "public-key");

/// <summary>
///     The result of an authentication ceremony (a <c>PublicKeyCredential</c> with an assertion response).
///     POST it to your backend, which verifies the signature against the stored public key.
/// </summary>
/// <param name="Id">Credential id (base64url).</param>
/// <param name="RawId">Credential raw id (base64url).</param>
/// <param name="ClientDataJson">Client data JSON (base64url).</param>
/// <param name="AuthenticatorData">Authenticator data (base64url) — signed by the credential.</param>
/// <param name="Signature">Assertion signature (base64url) — verify over authenticatorData ∥ hash(clientDataJSON).</param>
/// <param name="UserHandle">The user handle (base64url) for a discoverable credential, else <c>null</c>.</param>
/// <param name="Type">Credential type — <c>"public-key"</c>.</param>
public sealed record AssertionResult(
    string Id,
    string RawId,
    string ClientDataJson,
    string AuthenticatorData,
    string Signature,
    string? UserHandle,
    string Type = "public-key");

/// <summary>
///     Typed access to the Web Authentication API (WebAuthn / passkeys —
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Web_Authentication_API" />) — register
///     and sign in with a passkey (platform biometric or a roaming security key) instead of a password.
///     Inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         WebAuthn is a two-party protocol: your <b>backend</b> issues a random challenge and verifies the
///         returned attestation/assertion (the security depends on that server-side verification). This
///         wrapper covers the <b>browser</b> half — turning typed options into a
///         <c>navigator.credentials.create</c>/<c>get</c> call and handing back the response as base64url
///         strings ready to POST. Call from a <b>user-gesture handler</b>; gate on
///         <see cref="IsSupportedAsync" />. A user cancellation / timeout (<c>NotAllowedError</c>) returns
///         <c>null</c> rather than throwing. Works on <b>both transports</b>, though the authenticator UI
///         needs a live gesture — for installed-app flows prefer WASM.
///     </para>
/// </remarks>
public interface IWebAuthn
{
    /// <summary>Whether the browser supports WebAuthn (<c>window.PublicKeyCredential</c> present).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Whether a user-verifying <b>platform</b> authenticator (built-in biometric) is available — use it
    ///     to decide whether to offer "create a passkey on this device".
    /// </summary>
    ValueTask<bool> IsPlatformAuthenticatorAvailableAsync();

    /// <summary>
    ///     Registers a new passkey (<c>navigator.credentials.create</c>) and returns the attestation to send
    ///     to your backend, or <c>null</c> if the user cancelled. Must be called from a user-gesture handler.
    /// </summary>
    ValueTask<AttestationResult?> CreateAsync(PublicKeyCredentialCreationOptions options);

    /// <summary>
    ///     Authenticates with an existing passkey (<c>navigator.credentials.get</c>) and returns the assertion
    ///     to verify on your backend, or <c>null</c> if the user cancelled. Must be called from a user-gesture
    ///     handler.
    /// </summary>
    ValueTask<AssertionResult?> GetAsync(PublicKeyCredentialRequestOptions options);
}

/// <summary>
///     Default <see cref="IWebAuthn" />, backed by the unified <see cref="IJSRuntime" />. The
///     <c>ArrayBuffer</c>-heavy credential shapes can't be expressed through dotted identifiers, so options
///     and results go through the framework's <c>__raskWebAuthn</c> helper, which base64url-encodes the
///     binary fields at the boundary.
/// </summary>
public sealed class WebAuthn(IJSRuntime js) : IWebAuthn
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskWebAuthn.isSupported");

    /// <inheritdoc />
    public ValueTask<bool> IsPlatformAuthenticatorAvailableAsync() =>
        js.InvokeAsync<bool>("__raskWebAuthn.platformAuthenticatorAvailable");

    /// <inheritdoc />
    public ValueTask<AttestationResult?> CreateAsync(PublicKeyCredentialCreationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return js.InvokeAsync<AttestationResult?>("__raskWebAuthn.create", options);
    }

    /// <inheritdoc />
    public ValueTask<AssertionResult?> GetAsync(PublicKeyCredentialRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return js.InvokeAsync<AssertionResult?>("__raskWebAuthn.get", options);
    }
}
