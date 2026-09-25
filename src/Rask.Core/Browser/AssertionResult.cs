namespace Rask.Core.Browser;

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
