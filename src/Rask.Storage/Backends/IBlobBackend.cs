using System.Diagnostics.CodeAnalysis;

namespace Rask.Storage.Backends;

/// <summary>
/// The byte store behind <see cref="IFiles"/> — the small intersection a directory, an S3 bucket and an Azure
/// container all agree on. Keys reaching it have already passed <see cref="KeyLayout.IsValid"/>, and every
/// implementation checks again before a key becomes a path or a URL.
/// </summary>
internal interface IBlobBackend
{
    StorageProvider Provider { get; }

    /// <summary>The largest object one write can create. Validated against <see cref="StorageOptions.MaxFileSize"/> at startup.</summary>
    long MaxSinglePutBytes { get; }

    /// <summary>
    /// A fresh path, not yet created, for the caller to spool an upload into. The disk store hands out one
    /// beside its files, so storing is a rename; the remote stores hand out a temp file.
    /// </summary>
    string CreateSpoolPath();

    /// <summary>Stores the spooled file at <paramref name="sourcePath"/> under <paramref name="key"/>. The source may be consumed.</summary>
    Task PutFileAsync(string key, string sourcePath, long length, BlobHeaders headers, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the object from <paramref name="offset"/> — to its end, or for <paramref name="count"/> bytes — or
    /// returns <c>null</c> when it does not exist.
    /// </summary>
    Task<Stream?> OpenReadAsync(string key, long offset, long? count, CancellationToken cancellationToken);

    /// <summary>
    /// A seekable stream over an object of <paramref name="size"/> bytes, for the file routes' range handling.
    /// <paramref name="rangeFrom"/>/<paramref name="rangeTo"/> hint at the one range the request asked for, so a
    /// remote store can fetch just those bytes. Opens nothing until the first read, so <c>HEAD</c> and
    /// <c>304</c> never touch the store.
    /// </summary>
    Task<Stream?> OpenForServingAsync(string key, long size, long? rangeFrom, long? rangeTo, CancellationToken cancellationToken);

    /// <summary>Removes the object. Removing one that is not there is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>Every object under <paramref name="prefix"/>, read lazily, page by page.</summary>
    IAsyncEnumerable<BlobEntry> ListAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>
    /// A provider-signed URL that reads the object until <paramref name="lifetime"/> passes, served with the
    /// given type and disposition — or <c>false</c> when this store cannot sign one, in which case the app
    /// serves the file itself.
    /// </summary>
    bool TryPresign(string key, TimeSpan lifetime, string contentType, string contentDisposition,
        [NotNullWhen(true)] out string? url);

    /// <summary>Removes spool files older than <paramref name="olderThan"/> — what a crash mid-save leaves behind.</summary>
    Task DeleteStaleSpoolAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
}

/// <summary>One object in a listing.</summary>
internal readonly record struct BlobEntry(string Key, long Size, DateTimeOffset LastModified);

/// <summary>
/// The headers stored with an object, so that a CDN in front of a public bucket serves it with the same safe
/// type and disposition the app would.
/// </summary>
internal readonly record struct BlobHeaders(string ContentType, string ContentDisposition, string CacheControl);
