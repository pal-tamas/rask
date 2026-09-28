namespace Rask.Core.Browser;

/// <summary>A handle to one IndexedDB-backed key/value store (see <see cref="IIndexedDb.OpenStoreAsync" />).</summary>
public interface IKeyValueStore
{
    /// <summary>Stores <paramref name="value" /> under <paramref name="key" /> (overwriting any existing).</summary>
    ValueTask SetAsync(string key, string value);

    /// <summary>Reads the value for <paramref name="key" />, or <c>null</c> if absent.</summary>
    ValueTask<string?> GetAsync(string key);

    /// <summary>
    ///     Stores raw bytes under <paramref name="key" /> (overwriting any existing) — for content that is
    ///     binary rather than text, such as an image or a database file.
    /// </summary>
    /// <remarks>
    ///     IndexedDB stores these as a real <c>Uint8Array</c>, so a megabyte of bytes costs a megabyte of
    ///     quota. Base64 appears only in transit, because that is what crosses the JS interop boundary
    ///     reliably on every host.
    ///     <para>
    ///         The default implementation stores the base64 text through <see cref="SetAsync" />, so a store
    ///         written before this method existed still compiles and behaves correctly — it just pays the
    ///         ~33% size penalty in storage as well as on the wire. The built-in store overrides it.
    ///     </para>
    /// </remarks>
    ValueTask SetBytesAsync(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        return SetAsync(key, Convert.ToBase64String(value));
    }

    /// <summary>Reads the bytes for <paramref name="key" />, or <c>null</c> if absent.</summary>
    /// <remarks>Pairs with <see cref="SetBytesAsync" />; do not read a key written by <see cref="SetAsync" />.</remarks>
    async ValueTask<byte[]?> GetBytesAsync(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var value = await GetAsync(key).ConfigureAwait(false);
        return value is null ? null : Convert.FromBase64String(value);
    }

    /// <summary>Removes <paramref name="key" /> (a no-op if absent).</summary>
    ValueTask DeleteAsync(string key);

    /// <summary>All keys currently in the store.</summary>
    ValueTask<string[]> KeysAsync();

    /// <summary>Removes every entry in the store.</summary>
    ValueTask ClearAsync();
}
