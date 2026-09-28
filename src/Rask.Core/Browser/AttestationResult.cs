namespace Rask.Core.Browser;

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
