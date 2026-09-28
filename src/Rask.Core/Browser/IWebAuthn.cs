namespace Rask.Core.Browser;

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
