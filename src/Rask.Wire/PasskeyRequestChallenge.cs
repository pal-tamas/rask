namespace Rask.Wire;

/// <summary>What the browser needs to sign in with a passkey, and the sealed state that carries the challenge back.</summary>
/// <remarks>
/// No credential list: the ceremony is discoverable, so the authenticator offers the accounts it holds for this site
/// and the visitor types nothing. That is the whole point of passkey sign-in.
/// </remarks>
/// <param name="State">The sealed challenge, posted back with the result and never read by the client.</param>
/// <param name="Challenge">The challenge to sign, base64url.</param>
/// <param name="RelyingPartyId">The domain the passkey is bound to.</param>
/// <param name="TimeoutMs">How long the browser should wait for the user.</param>
public sealed record PasskeyRequestChallenge(
    string State,
    string Challenge,
    string RelyingPartyId,
    int TimeoutMs);
