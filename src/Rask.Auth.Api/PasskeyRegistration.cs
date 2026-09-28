namespace Rask.Auth;

/// <summary>What a verified registration yields: everything needed to store the credential.</summary>
internal readonly record struct PasskeyRegistration(
    byte[] CredentialId, byte[] PublicKey, int Algorithm, uint SignCount, bool BackedUp);
