namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Web Crypto API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Crypto" />) — cryptographically strong
///     randomness (UUIDs, nonces) and hashing, from the browser's native implementation. Works on
///     <b>both transports</b>; inject it through a component constructor.
/// </summary>
/// <remarks>
///     Requires a secure context (HTTPS or localhost) — <c>crypto.subtle</c> is unavailable on insecure
///     origins. This wraps the safe, common primitives; full key generation / sign / encrypt is out of
///     scope (do that server-side).
/// </remarks>
public interface ICrypto
{
    /// <summary>A new random v4 UUID (<c>crypto.randomUUID()</c>), e.g. for a client-side id.</summary>
    ValueTask<string> RandomUuidAsync();

    /// <summary>
    ///     <paramref name="length" /> cryptographically strong random bytes
    ///     (<c>crypto.getRandomValues</c>), e.g. for a nonce or token.
    /// </summary>
    ValueTask<byte[]> RandomBytesAsync(int length);

    /// <summary>
    ///     Hashes <paramref name="text" /> (UTF-8) with <paramref name="algorithm" />
    ///     (<c>crypto.subtle.digest</c>) and returns the digest as a lowercase hex string.
    /// </summary>
    ValueTask<string> DigestHexAsync(HashAlgorithm algorithm, string text);
}
