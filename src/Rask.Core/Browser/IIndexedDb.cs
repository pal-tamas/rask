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
