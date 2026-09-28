namespace Rask.Auth;

/// <summary>Which ceremony a challenge was issued for.</summary>
internal enum PasskeyPurpose
{
    /// <summary>Adding a passkey to a signed-in account.</summary>
    Create = 1,

    /// <summary>Signing in with one.</summary>
    Get = 2,
}
