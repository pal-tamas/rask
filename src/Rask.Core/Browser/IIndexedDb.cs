using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to a persistent, asynchronous key/value store backed by IndexedDB
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/IndexedDB_API" />) — far larger than
///     <see cref="IBrowserStorage" /> (hundreds of MB vs ~5 MB) and non-blocking, for caching app data
///     offline. Inject it through a component constructor, open a named store, and read/write string values
///     (serialize your own objects to JSON).
/// </summary>
/// <remarks>
///     This wraps the common key/value use of IndexedDB — a named store of string values. The full API
///     (multiple object stores per database, indexes, cursors, versioned schema migrations) is out of
///     scope. Each store is its own IndexedDB database with a single object store; the framework caches the
///     open connection. Works on <b>both transports</b>; requires a secure context for some browsers in
///     private mode.
/// </remarks>
public interface IIndexedDb
{
    /// <summary>Whether the browser supports IndexedDB (<c>"indexedDB" in window</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Opens (creating if needed) the key/value store <paramref name="name" /> and returns a handle for
    ///     reading and writing it. Cheap to call repeatedly — the underlying connection is cached.
    /// </summary>
    ValueTask<IKeyValueStore> OpenStoreAsync(string name);
}

/// <summary>A handle to one IndexedDB-backed key/value store (see <see cref="IIndexedDb.OpenStoreAsync" />).</summary>
public interface IKeyValueStore
{
    /// <summary>Stores <paramref name="value" /> under <paramref name="key" /> (overwriting any existing).</summary>
    ValueTask SetAsync(string key, string value);

    /// <summary>Reads the value for <paramref name="key" />, or <c>null</c> if absent.</summary>
    ValueTask<string?> GetAsync(string key);

    /// <summary>Removes <paramref name="key" /> (a no-op if absent).</summary>
    ValueTask DeleteAsync(string key);

    /// <summary>All keys currently in the store.</summary>
    ValueTask<string[]> KeysAsync();

    /// <summary>Removes every entry in the store.</summary>
    ValueTask ClearAsync();
}

/// <summary>
///     Default <see cref="IIndexedDb" />, backed by the unified <see cref="IJSRuntime" />. IndexedDB's
///     request/transaction model can't be expressed through dotted <see cref="IJSRuntime" /> identifiers,
///     so all access goes through the framework's <c>__raskIdb</c> helper, which opens/caches the database
///     and wraps each operation in a transaction-scoped Promise.
/// </summary>
public sealed class IndexedDb(IJSRuntime js) : IIndexedDb
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskIdb.isSupported");

    /// <inheritdoc />
    public async ValueTask<IKeyValueStore> OpenStoreAsync(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        await js.InvokeVoidAsync("__raskIdb.open", name);
        return new Store(js, name);
    }

    private sealed class Store(IJSRuntime js, string name) : IKeyValueStore
    {
        public ValueTask SetAsync(string key, string value)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            return js.InvokeVoidAsync("__raskIdb.set", name, key, value);
        }

        public ValueTask<string?> GetAsync(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return js.InvokeAsync<string?>("__raskIdb.get", name, key);
        }

        public ValueTask DeleteAsync(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return js.InvokeVoidAsync("__raskIdb.delete", name, key);
        }

        public ValueTask<string[]> KeysAsync() => js.InvokeAsync<string[]>("__raskIdb.keys", name);

        public ValueTask ClearAsync() => js.InvokeVoidAsync("__raskIdb.clear", name);
    }
}
