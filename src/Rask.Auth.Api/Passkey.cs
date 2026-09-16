using Rask.Data;

namespace Rask.Auth;

/// <summary>
/// One passkey: a public key an authenticator holds the private half of, and signs a challenge with.
/// </summary>
/// <remarks>
/// <para>
/// A passkey is another way to sign in, beside the password. The private key never leaves the authenticator (a phone's
/// secure element, a laptop's Touch ID, a security key), so there is nothing here an attacker could steal and reuse:
/// the public key only checks signatures.
/// </para>
/// <para>
/// Read them like sessions to list what a user has: <c>Passkey.Where(p =&gt; p.UserId == id)</c>. Rask.Auth adds and
/// removes them; an app does not.
/// </para>
/// </remarks>
public sealed class Passkey : Aggregate<Guid>
{
    /// <summary>The user this passkey signs in.</summary>
    public Guid UserId { get; private set; }

    /// <summary>What the user called it — "MacBook", "iPhone", "YubiKey".</summary>
    public string Name { get; private set; } = "";

    /// <summary>The credential id the authenticator chose. Unique.</summary>
    public byte[] CredentialId { get; private set; } = [];

    /// <summary>How the browser said it can reach this authenticator: <c>internal</c>, <c>usb</c>, <c>hybrid</c>…</summary>
    public string? Transports { get; private set; }

    /// <summary>Whether the authenticator says the key is backed up, which is what makes a passkey synced.</summary>
    public bool BackedUp { get; private set; }

    /// <summary>When it last signed in, or <see langword="null" /> while it never has.</summary>
    public DateTime? LastUsedAt { get; private set; }

    /// <summary>The COSE algorithm the key signs with: -7 (ES256) or -257 (RS256).</summary>
    internal int Algorithm { get; private set; }

    // The credential public key, as the authenticator encoded it. Internal, like a password hash: it is not secret, but
    // nothing that serializes a Passkey's public properties needs to carry it.
    internal byte[] PublicKey { get; private set; } = [];

    /// <summary>
    /// The authenticator's own signature counter, when it keeps one.
    /// </summary>
    /// <remarks>
    /// A counter that fails to increase means two authenticators are answering for one credential — a clone. Synced
    /// passkeys keep no counter and report 0 forever, which is why 0 is not treated as a regression.
    /// </remarks>
    internal uint SignCount { get; private set; }

    /// <summary>The longest name kept, in characters.</summary>
    internal const int NameLength = 100;

    /// <summary>The longest transports string kept, in characters.</summary>
    internal const int TransportsLength = 128;

    /// <summary>The longest credential id WebAuthn allows, in bytes.</summary>
    internal const int CredentialIdLength = 1023;

    /// <summary>A bound on the stored COSE key, in bytes. Comfortably past an RSA-8192 key.</summary>
    internal const int PublicKeyLength = 2048;

    internal static Passkey Register(
        Guid userId,
        string? name,
        byte[] credentialId,
        byte[] publicKey,
        int algorithm,
        uint signCount,
        bool backedUp,
        string? transports,
        DateTime now)
    {
        var passkey = new Passkey
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = Clean(name) is { Length: > 0 } given ? given : "Passkey",
            CredentialId = credentialId,
            PublicKey = publicKey,
            Algorithm = algorithm,
            SignCount = signCount,
            BackedUp = backedUp,
            Transports = Truncate(transports, TransportsLength),
            LastUsedAt = now,
        };

        passkey.Raise(new PasskeyAdded(userId, passkey.Id, passkey.Name));
        return passkey;
    }

    internal void Used(uint signCount, DateTime now)
    {
        // Only ever forward: a counter that went backwards is refused before this is called.
        if (signCount > SignCount)
        {
            SignCount = signCount;
        }

        LastUsedAt = now;
    }

    internal void Removed() => Raise(new PasskeyRemoved(UserId, Id, Name));

    private static string Clean(string? name) =>
        Truncate(name?.Trim(), NameLength) ?? "";

    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}
