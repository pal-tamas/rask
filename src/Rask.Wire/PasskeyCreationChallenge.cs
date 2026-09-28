namespace Rask.Wire;

/// <summary>
/// What the browser needs to create a passkey, and the sealed state that carries the challenge back.
/// </summary>
/// <remarks>
/// Every binary field is base64url, which is how the browser's own WebAuthn API takes and returns them, so a
/// TypeScript front end can pass these straight to <c>navigator.credentials.create</c> after decoding.
/// <see cref="State" /> is opaque: it is the challenge, signed and expiring, and the server will not accept a
/// registration without it.
/// </remarks>
/// <param name="State">The sealed challenge, posted back with the result and never read by the client.</param>
/// <param name="Challenge">The challenge to sign, base64url.</param>
/// <param name="RelyingPartyId">The domain the passkey is bound to.</param>
/// <param name="RelyingPartyName">The site name the platform UI shows.</param>
/// <param name="UserId">The user handle, base64url — the account id, never the address.</param>
/// <param name="UserName">What the platform UI lists the account as.</param>
/// <param name="UserDisplayName">The friendlier name beside it.</param>
/// <param name="ExcludeCredentials">Credential ids the account already has, base64url, so one key is not added twice.</param>
/// <param name="TimeoutMs">How long the browser should wait for the user.</param>
public sealed record PasskeyCreationChallenge(
    string State,
    string Challenge,
    string RelyingPartyId,
    string RelyingPartyName,
    string UserId,
    string UserName,
    string UserDisplayName,
    IReadOnlyList<string> ExcludeCredentials,
    int TimeoutMs);
