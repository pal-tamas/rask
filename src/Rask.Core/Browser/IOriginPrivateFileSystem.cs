using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Origin Private File System
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/File_System_API/Origin_private_file_system" />) —
///     a private, persistent, origin-scoped file tree the app owns outright. Unlike
///     <see cref="IFileSystemAccess" /> there is no picker and no user gesture: the app addresses files by
///     path and reopens the same paths on every visit, which is what makes it the right home for a local
///     database file, a downloaded bundle, or any blob too large for <see cref="IIndexedDb" />. Inject it
///     through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         Paths are <c>/</c>-separated and relative to the origin's private root (<c>"db/app.sqlite"</c>).
///         Parent directories are created on write and never have to be made explicitly. Nothing here is
///         visible to the user's real file system, and nothing is shared with another origin.
///     </para>
///     <para>
///         Reads and writes take a byte <b>offset</b> so a large file can be worked in chunks without ever
///         materialising the whole thing — the payload crossing the interop boundary is bounded by the range
///         you ask for. <see cref="ReadAllBytesAsync" /> / <see cref="WriteAllBytesAsync" /> are the
///         single-round-trip convenience over the same store; prefer the ranged calls once a file grows past
///         a few megabytes.
///     </para>
///     <para>
///         Reading a path that does not exist returns <c>null</c> rather than throwing, matching
///         <see cref="IKeyValueStore.GetAsync" />. A ranged read that runs past the end of the file returns
///         the bytes that were available (a short read), exactly as an ordinary file read would.
///     </para>
///     <para>
///         Works on <b>both transports</b>, but every call is a round trip: under the Server transport that
///         round trip crosses the WebSocket, so the local-database scenario this API exists for is in
///         practice a WASM one. Requires a secure context; gate on <see cref="IsSupportedAsync" />.
///     </para>
///     <para>
///         <b>Durability.</b> OPFS is persistent but not automatically exempt from eviction — a browser may
///         reclaim it under storage pressure. Call
///         <see cref="IStorageEstimator.RequestPersistAsync" /> to ask for the origin to be exempted, and
///         treat unsynced writes as at risk until it reports <c>true</c>.
///     </para>
/// </remarks>
public interface IOriginPrivateFileSystem
{
    /// <summary>Whether the browser exposes OPFS (<c>navigator.storage.getDirectory</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Whether a file exists at <paramref name="path" />.</summary>
    ValueTask<bool> ExistsAsync(string path);

    /// <summary>The size of the file at <paramref name="path" /> in bytes, or <c>null</c> if it does not exist.</summary>
    ValueTask<long?> GetSizeAsync(string path);

    /// <summary>
    ///     Reads up to <paramref name="count" /> bytes starting at <paramref name="offset" />, or <c>null</c>
    ///     if the file does not exist. Returns fewer bytes than asked for when the range runs past the end.
    /// </summary>
    ValueTask<byte[]?> ReadAsync(string path, long offset, int count);

    /// <summary>
    ///     Writes <paramref name="bytes" /> at <paramref name="offset" />, leaving the rest of the file
    ///     intact, and creating the file (and any parent directories) if needed. Writing past the current end
    ///     extends the file, zero-filling the gap.
    /// </summary>
    ValueTask WriteAsync(string path, long offset, byte[] bytes);

    /// <summary>
    ///     Resizes the file at <paramref name="path" /> to <paramref name="size" /> bytes — truncating it, or
    ///     extending it with zeroes. Creates the file if it does not exist.
    /// </summary>
    ValueTask TruncateAsync(string path, long size);

    /// <summary>Reads the whole file, or <c>null</c> if it does not exist.</summary>
    ValueTask<byte[]?> ReadAllBytesAsync(string path);

    /// <summary>Replaces the whole file with <paramref name="bytes" />, creating it (and parents) if needed.</summary>
    ValueTask WriteAllBytesAsync(string path, byte[] bytes);

    /// <summary>
    ///     Removes the file or directory at <paramref name="path" /> (a no-op if absent). A non-empty
    ///     directory needs <paramref name="recursive" />.
    /// </summary>
    ValueTask DeleteAsync(string path, bool recursive = false);

    /// <summary>
    ///     The entry names directly inside <paramref name="path" /> (the private root when omitted), or an
    ///     empty array if that directory does not exist.
    /// </summary>
    ValueTask<string[]> ListAsync(string path = "");
}

/// <summary>
///     Default <see cref="IOriginPrivateFileSystem" />, backed by the unified <see cref="IJSRuntime" />.
///     OPFS handles are opaque and cannot cross the interop boundary, and every operation needs the path
///     walked from the private root, so all access goes through the framework's <c>__raskOpfs</c> helper.
///     Bytes ride the boundary base64-encoded, as they do for <see cref="IFileSystemAccess" />.
/// </summary>
public sealed class OriginPrivateFileSystem(IJSRuntime js) : IOriginPrivateFileSystem
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskOpfs.isSupported");

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return js.InvokeAsync<bool>("__raskOpfs.exists", path);
    }

    /// <inheritdoc />
    public ValueTask<long?> GetSizeAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return js.InvokeAsync<long?>("__raskOpfs.size", path);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> ReadAsync(string path, long offset, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var base64 = await js.InvokeAsync<string?>("__raskOpfs.read", path, offset, count);
        return base64 is null ? null : Convert.FromBase64String(base64);
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(string path, long offset, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentNullException.ThrowIfNull(bytes);

        return js.InvokeVoidAsync("__raskOpfs.write", path, offset, Convert.ToBase64String(bytes));
    }

    /// <inheritdoc />
    public ValueTask TruncateAsync(string path, long size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        return js.InvokeVoidAsync("__raskOpfs.truncate", path, size);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> ReadAllBytesAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var base64 = await js.InvokeAsync<string?>("__raskOpfs.readAll", path);
        return base64 is null ? null : Convert.FromBase64String(base64);
    }

    /// <inheritdoc />
    public ValueTask WriteAllBytesAsync(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);

        return js.InvokeVoidAsync("__raskOpfs.writeAll", path, Convert.ToBase64String(bytes));
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(string path, bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return js.InvokeVoidAsync("__raskOpfs.delete", path, recursive);
    }

    /// <inheritdoc />
    public ValueTask<string[]> ListAsync(string path = "")
    {
        ArgumentNullException.ThrowIfNull(path);
        return js.InvokeAsync<string[]>("__raskOpfs.list", path);
    }
}
