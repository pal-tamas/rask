namespace Rask.Auth;

/// <summary>What the browser must prove against: the relying party, the origins it may answer from, and the challenge.</summary>
/// <param name="RelyingPartyId">The RP id the authenticator hashed — a registrable domain, no scheme and no port.</param>
/// <param name="Origins">The origins a ceremony may come from, compared exactly.</param>
/// <param name="Challenge">The random challenge this ceremony was started with.</param>
internal sealed record PasskeyCeremony(string RelyingPartyId, IReadOnlyList<string> Origins, byte[] Challenge);
