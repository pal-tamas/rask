namespace Rask.Auth;

/// <summary>What a verified assertion yields: the authenticator's new signature counter.</summary>
internal readonly record struct PasskeyAssertion(uint SignCount);
