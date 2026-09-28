namespace Rask.Auth;

/// <summary>What checking a password against a stored hash found.</summary>
internal enum PasswordCheck
{
    /// <summary>The password does not match, or the hash is not one this hasher reads.</summary>
    Failed,

    /// <summary>The password matches.</summary>
    Success,

    /// <summary>The password matches, and the hash should be replaced with one under the current settings.</summary>
    SuccessRehashNeeded,
}
