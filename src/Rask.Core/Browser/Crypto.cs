using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
