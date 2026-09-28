namespace Rask.Wire;

/// <summary>A created passkey, on its way to be verified and stored.</summary>
/// <param name="State">The sealed challenge the ceremony started with.</param>
/// <param name="Name">What to call it in the account's device list.</param>
/// <param name="RawId">The credential id, base64url.</param>
/// <param name="ClientDataJson">The client data, base64url.</param>
/// <param name="AttestationObject">The attestation object, base64url.</param>
/// <param name="Transports">How the browser says it can reach this authenticator.</param>
public sealed record PasskeyRegistrationRequest(
    string State,
    string? Name,
    string RawId,
    string ClientDataJson,
    string AttestationObject,
    IReadOnlyList<string>? Transports = null);
