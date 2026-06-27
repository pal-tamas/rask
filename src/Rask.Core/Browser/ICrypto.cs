using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>A hash algorithm supported by <c>crypto.subtle.digest</c>.</summary>
public enum HashAlgorithm
{
    /// <summary><c>SHA-1</c> — legacy; not collision-resistant. Avoid for security.</summary>
    Sha1,

    /// <summary><c>SHA-256</c>.</summary>
    Sha256,

    /// <summary><c>SHA-384</c>.</summary>
    Sha384,

    /// <summary><c>SHA-512</c>.</summary>
    Sha512
}

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

/// <summary>
///     Default <see cref="ICrypto" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>getRandomValues</c> fills a typed array and <c>subtle.digest</c> returns an <c>ArrayBuffer</c>, so
///     both go through the framework's <c>__raskCrypto</c> helper, which returns plain bytes / a hex string.
/// </summary>
public sealed class Crypto(IJSRuntime js) : ICrypto
{
    /// <inheritdoc />
    public ValueTask<string> RandomUuidAsync() => js.InvokeAsync<string>("__raskCrypto.randomUuid");

    /// <inheritdoc />
    public ValueTask<byte[]> RandomBytesAsync(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return js.InvokeAsync<byte[]>("__raskCrypto.randomBytes", length);
    }

    /// <inheritdoc />
    public ValueTask<string> DigestHexAsync(HashAlgorithm algorithm, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return js.InvokeAsync<string>("__raskCrypto.digestHex", ToSpecName(algorithm), text);
    }

    // crypto.subtle.digest uses these hyphenated names.
    private static string ToSpecName(HashAlgorithm algorithm) => algorithm switch
    {
        HashAlgorithm.Sha1 => "SHA-1",
        HashAlgorithm.Sha256 => "SHA-256",
        HashAlgorithm.Sha384 => "SHA-384",
        HashAlgorithm.Sha512 => "SHA-512",
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown hash algorithm.")
    };
}
