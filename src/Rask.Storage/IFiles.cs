using Microsoft.AspNetCore.Http;
using Rask.Core.Forms;

namespace Rask.Storage;

/// <summary>
/// The application's stored files: save an upload, then hand it back as a public URL, a temporary URL, or a
/// download behind the app's own authorization.
/// </summary>
/// <remarks>
/// <para>
/// Every saved file is a <see cref="StoredFile"/> row on the application's database plus its bytes in the
/// configured store — a directory, an S3-compatible bucket or an Azure container. Keep the
/// <see cref="StoredFile.Id"/> on your own entity; everything here is addressed by it.
/// </para>
/// <para>
/// <b>The bytes are written before the row.</b> A save that fails half way leaves bytes with no row, never a
/// row with no bytes, and the orphaned bytes are removed by a background sweep after
/// <see cref="StorageOptions.OrphanGracePeriod"/>. The row is committed on its own context, not inside a
/// transaction your handler has open — a row whose entity you then fail to save is yours to delete.
/// </para>
/// </remarks>
public interface IFiles
{
    /// <summary>
    /// Stores an upload and records it, returning the row. The content type is sniffed from the bytes; the
    /// browser's claim is ignored.
    /// </summary>
    /// <exception cref="FileRejectedException">
    /// The file is larger than <see cref="StorageOptions.MaxFileSize"/>, or its content is not in
    /// <see cref="StorageOptions.AllowedTypes"/>. Nothing is stored.
    /// </exception>
    Task<StoredFile> SaveAsync(RaskFile file, Action<SaveOptions>? configure = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores <paramref name="content"/> under the display name <paramref name="name"/> and records it. The
    /// stream is read to its end and not disposed.
    /// </summary>
    /// <exception cref="FileRejectedException">
    /// The content is larger than <see cref="StorageOptions.MaxFileSize"/>, or its type is not in
    /// <see cref="StorageOptions.AllowedTypes"/>. Nothing is stored.
    /// </exception>
    Task<StoredFile> SaveAsync(Stream content, string name, Action<SaveOptions>? configure = null,
        CancellationToken cancellationToken = default);

    /// <summary>The row for <paramref name="id"/>, or <c>null</c> when there is none.</summary>
    Task<StoredFile?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the file's bytes for reading, or returns <c>null</c> when there is no such file. The caller owns
    /// the stream.
    /// </summary>
    Task<Stream?> OpenReadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the file: the row first, then its bytes. Returns <c>false</c> when there was no such file.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The public URL of a file saved with <see cref="SaveOptions.Public"/>. Reads nothing: it is built from
    /// the id, so it costs nothing inside a render.
    /// </summary>
    /// <remarks>
    /// Under <see cref="StorageOptions.PublicBaseUrl"/> when one is configured (a CDN or a public bucket
    /// domain); otherwise the app serves it at <c>/_rask/files/public/{id}</c>, which answers
    /// <c>404</c> for a file that is not public. Requires <c>app.MapRaskStorage()</c>.
    /// </remarks>
    string Url(Guid id);

    /// <summary>
    /// A URL for any file that stops working after <paramref name="lifetime"/> (at most 7 days), or
    /// <c>null</c> when there is no such file.
    /// </summary>
    /// <remarks>
    /// On S3 and on Azure with an account key this is the provider's own signed URL, so the download never
    /// passes through the app — and it cannot be revoked before it expires. Otherwise it is a signed link to
    /// <c>/_rask/files/{token}</c>, which stops working as soon as the file is deleted.
    /// </remarks>
    Task<string?> TemporaryUrlAsync(Guid id, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>
    /// A response that streams the file, for an endpoint that has already checked the caller may see it.
    /// Answers <c>404</c> when there is no such file, and supports ranges and conditional requests.
    /// </summary>
    IResult Download(Guid id);
}
