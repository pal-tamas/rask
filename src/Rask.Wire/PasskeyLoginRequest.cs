namespace Rask.Wire;

/// <summary>A signed challenge, on its way to be verified into a session.</summary>
/// <param name="State">The sealed challenge the ceremony started with.</param>
/// <param name="RawId">The credential id, base64url.</param>
/// <param name="ClientDataJson">The client data, base64url.</param>
/// <param name="AuthenticatorData">The authenticator data, base64url.</param>
/// <param name="Signature">The signature over the authenticator data and the client-data hash, base64url.</param>
/// <param name="UserHandle">The user handle the authenticator returned, base64url, for a discoverable credential.</param>
/// <param name="Remember">Whether the session should outlive the browser session.</param>
public sealed record PasskeyLoginRequest(
    string State,
    string RawId,
    string ClientDataJson,
    string AuthenticatorData,
    string Signature,
    string? UserHandle = null,
    bool Remember = false);
