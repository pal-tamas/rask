using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Rask.Auth;

/// <summary>Which ceremony a challenge was issued for.</summary>
internal enum PasskeyPurpose
{
    /// <summary>Adding a passkey to a signed-in account.</summary>
    Create = 1,

    /// <summary>Signing in with one.</summary>
    Get = 2,
}

/// <summary>
/// The challenge half of a passkey ceremony: random, sealed, short-lived, and good exactly once.
/// </summary>
/// <remarks>
/// <para>
/// A WebAuthn ceremony is two requests — "give me a challenge", then "here is what I signed" — and the server has to
/// remember the challenge in between. Rask keeps nothing: the challenge travels back to the client sealed with Data
/// Protection, carrying its purpose, the account it was issued to and its expiry, exactly like the reset links in
/// <see cref="AuthTokens" />. No table, no distributed cache, and no sticky sessions needed to sign in.
/// </para>
/// <para>
/// Sealed state alone would let a captured ceremony be replayed until it expired, so a redeemed state is also
/// remembered, briefly, and refused the second time. That memory is per process: behind several replicas a replay
/// would have to land on a different one inside the ceremony's lifetime, and the authenticator's own signature
/// counter catches it for every authenticator that keeps one. The window is a minute by design.
/// </para>
/// </remarks>
internal sealed class PasskeyChallenges(IDataProtectionProvider provider, TimeProvider clock)
{
    private const string ProtectorPurpose = "Rask.Auth.Passkeys.v1";

    /// <summary>How long a ceremony may take. The browser's own timeout is set to match.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>Challenge length. 32 bytes is what the spec calls for, and more than enough to be unguessable.</summary>
    private const int ChallengeLength = 32;

    private readonly IDataProtector _protector = provider.CreateProtector(ProtectorPurpose);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _redeemed = new(StringComparer.Ordinal);

    /// <summary>A fresh challenge.</summary>
    public static byte[] NewChallenge() => RandomNumberGenerator.GetBytes(ChallengeLength);

    /// <summary>Seals <paramref name="challenge" /> into the state the client posts back.</summary>
    /// <param name="purpose">Which ceremony it is for.</param>
    /// <param name="userId">The account adding a passkey, or <see cref="Guid.Empty" /> for a sign-in.</param>
    /// <param name="challenge">The challenge bytes.</param>
    public string Issue(PasskeyPurpose purpose, Guid userId, byte[] challenge)
    {
        var expires = (clock.GetUtcNow() + Lifetime).ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)purpose}|{userId:N}|{expires}|{WebEncoders.Base64UrlEncode(challenge)}"));

        return WebEncoders.Base64UrlEncode(_protector.Protect(payload));
    }

    /// <summary>
    /// Opens a state once, returning the challenge it carried, or <see langword="null" /> when it is not this
    /// server's, not for this ceremony, expired, or already used.
    /// </summary>
    /// <param name="state">What the client posted back.</param>
    /// <param name="purpose">The ceremony being completed.</param>
    /// <param name="userId">The account the challenge was issued to.</param>
    public byte[]? Redeem(string? state, PasskeyPurpose purpose, out Guid userId)
    {
        userId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(_protector.Unprotect(WebEncoders.Base64UrlDecode(state)));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }

        var parts = payload.Split('|');
        if (parts.Length != 4
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var issued)
            || issued != (int)purpose
            || !Guid.TryParseExact(parts[1], "N", out var owner)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        if (DateTimeOffset.FromUnixTimeSeconds(expires) <= now)
        {
            return null;
        }

        byte[] challenge;
        try
        {
            challenge = WebEncoders.Base64UrlDecode(parts[3]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (challenge.Length != ChallengeLength || !MarkRedeemed(state, now))
        {
            return null;
        }

        userId = owner;
        return challenge;
    }

    // The state itself is the identity of the ceremony, and it is already unguessable, so its hash is the key. A
    // second use finds the entry and is refused.
    private bool MarkRedeemed(string state, DateTimeOffset now)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        var expires = now + Lifetime;

        if (!_redeemed.TryAdd(key, expires))
        {
            return false;
        }

        // Nothing here outlives a ceremony, so the table is swept whenever it grows past what a busy minute looks like.
        if (_redeemed.Count > 10_000)
        {
            foreach (var (redeemed, at) in _redeemed)
            {
                if (at <= now)
                {
                    _redeemed.TryRemove(redeemed, out _);
                }
            }
        }

        return true;
    }
}
