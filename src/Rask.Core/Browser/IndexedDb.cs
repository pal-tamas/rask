using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IIndexedDb" />, backed by the unified <see cref="IJSRuntime" />. IndexedDB's
///     request/transaction model can't be expressed through dotted <see cref="IJSRuntime" /> identifiers,
///     so all access goes through the framework's <c>__raskIdb</c> helper, which opens/caches the database
///     and wraps each operation in a transaction-scoped Promise.
/// </summary>
public sealed class IndexedDb(IJSRuntime js) : IIndexedDb
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupported() => js.InvokeAsync<bool>("__raskIdb.isSupported");

    /// <inheritdoc />
    public async ValueTask<IKeyValueStore> OpenStore(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        await js.InvokeVoidAsync("__raskIdb.open", name);
        return new Store(js, name);
    }

    private sealed class Store(IJSRuntime js, string name) : IKeyValueStore
    {
        public ValueTask Set(string key, string value)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            return js.InvokeVoidAsync("__raskIdb.set", name, key, value);
        }

        public ValueTask<string?> Get(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return js.InvokeAsync<string?>("__raskIdb.get", name, key);
        }

        // Base64 crosses the boundary; the helper decodes it to a Uint8Array before it reaches the
        // object store, so the ~33% inflation is paid on the wire but never against the quota.
        public ValueTask SetBytes(string key, byte[] value)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            return js.InvokeVoidAsync("__raskIdb.setBytes", name, key, Convert.ToBase64String(value));
        }

        public async ValueTask<byte[]?> GetBytes(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            var base64 = await js.InvokeAsync<string?>("__raskIdb.getBytes", name, key).ConfigureAwait(false);
            return base64 is null ? null : Convert.FromBase64String(base64);
        }

        public ValueTask Delete(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return js.InvokeVoidAsync("__raskIdb.delete", name, key);
        }

        public ValueTask<string[]> Keys() => js.InvokeAsync<string[]>("__raskIdb.keys", name);

        public ValueTask Clear() => js.InvokeVoidAsync("__raskIdb.clear", name);
    }
}
