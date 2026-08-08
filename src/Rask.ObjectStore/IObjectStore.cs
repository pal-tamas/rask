namespace Rask.ObjectStore;

/// <summary>One object returned by <see cref="IObjectStore.ListAsync" />.</summary>
/// <param name="Key">The object's full key, including any prefix it was listed under.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="LastModified">When the store last wrote it.</param>
/// <param name="ETag">The store's entity tag, quotes stripped.</param>
public sealed record ObjectEntry(string Key, long Size, DateTimeOffset LastModified, string? ETag);

/// <summary>
///     A bucket or container, addressed by key. Deliberately the small intersection every object store
///     agrees on — ranged read, write, conditional create, prefix list, delete — so the same calls work
///     against S3, R2, GCS, MinIO, B2, Spaces and Azure Blob without a per-provider branch at the call
///     site.
/// </summary>
/// <remarks>
///     <para>
///         <b>Ranged reads are the point.</b> Object storage charges and waits per byte transferred, so a
///         caller that only needs part of a large object should ask for that part.
///         <see cref="OpenReadAsync" /> exists for the whole-object case and streams rather than buffering,
///         so a multi-gigabyte snapshot never has to fit in memory.
///     </para>
///     <para>
///         <b>Missing objects return <c>null</c>, they do not throw.</b> "Not there" is an ordinary answer
///         for a store used as a cache, a log, or a sync target — it should not need a <c>catch</c>.
///         Genuine failures (auth, network, a 5xx) still throw.
///     </para>
///     <para>
///         <b>A key is a path.</b> Slashes separate segments and everything else in a segment is escaped.
///         One consequence is worth knowing: a key whose <em>name</em> contains an encoded slash
///         (<c>%2F</c>) cannot be addressed, because <see cref="Uri" /> normalises that back to a real
///         separator before any of this code sees it. Such keys are legal in S3 and unreachable here.
///     </para>
///     <para>
///         <b>In a browser the bucket must allow the app's origin through CORS.</b> Every provider requires
///         this and none of them do it by default; without it, calls fail in a way the browser deliberately
///         makes opaque. This is the single most common reason a working server-side configuration does
///         nothing from a page.
///     </para>
/// </remarks>
public interface IObjectStore
{
    /// <summary>
    ///     Reads up to <paramref name="count" /> bytes of <paramref name="key" /> starting at
    ///     <paramref name="offset" />, or <c>null</c> if the object does not exist. Returns fewer bytes than
    ///     asked for when the range runs past the end.
    /// </summary>
    Task<byte[]?> GetRangeAsync(string key, long offset, int count, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Opens the whole object for reading, or returns <c>null</c> if it does not exist. The caller owns
    ///     the stream. Prefer this to <see cref="GetRangeAsync" /> when you want everything and the object
    ///     may be large — nothing is buffered.
    /// </summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="content" /> to <paramref name="key" />, replacing any existing object.</summary>
    Task PutAsync(string key, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Writes <paramref name="length" /> bytes from <paramref name="content" /> to
    ///     <paramref name="key" />, replacing any existing object. The stream is sent as it is read, so an
    ///     arbitrarily large object costs no memory.
    /// </summary>
    Task PutAsync(string key, Stream content, long length, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Writes <paramref name="key" /> only if no object exists there, returning whether this call was
    ///     the one that created it.
    /// </summary>
    /// <remarks>
    ///     This is the portable mutual-exclusion primitive: an atomic compare-and-create, implemented with
    ///     <c>If-None-Match: *</c>, supported by S3, Azure Blob and GCS alike. Where a distributed lock is
    ///     needed — electing one writer, running a compaction round once — having exactly one caller observe
    ///     <c>true</c> is enough, and unlike a lease it needs no renewal and leaks nothing if the winner
    ///     disappears.
    /// </remarks>
    Task<bool> TryCreateAsync(string key, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Every object whose key starts with <paramref name="prefix" />, oldest key first. Pagination is
    ///     followed internally, so the result is complete.
    /// </summary>
    Task<IReadOnlyList<ObjectEntry>> ListAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Removes <paramref name="key" />. Deleting an object that isn't there is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
