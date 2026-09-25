namespace Rask.Auth;

/// <summary>The flag byte of authenticator data (WebAuthn L2 §6.1).</summary>
[Flags]
internal enum AuthenticatorBits : byte
{
    /// <summary>Nothing set.</summary>
    None = 0,

    /// <summary>UP — somebody was there and touched it.</summary>
    UserPresent = 1 << 0,

    /// <summary>UV — and proved who they were, with a biometric or a PIN.</summary>
    UserVerified = 1 << 2,

    /// <summary>BE — the credential may be backed up.</summary>
    BackupEligible = 1 << 3,

    /// <summary>BS — the credential is backed up, which is what makes a passkey synced across a user's devices.</summary>
    BackedUp = 1 << 4,

    /// <summary>AT — attested credential data follows, which a registration carries and an assertion does not.</summary>
    AttestedCredentialData = 1 << 6,

    /// <summary>ED — extension data follows.</summary>
    ExtensionData = 1 << 7,
}
