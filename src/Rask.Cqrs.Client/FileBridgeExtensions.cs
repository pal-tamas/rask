using Rask.Core.Forms;
using Rask.Core.Routing;

namespace Rask.Cqrs.Client;

/// <summary>
///     The two conversions that keep <c>HttpClient</c> out of a file's round trip: the file a user
///     picked becomes a message property, and the file a message answered with reaches the user's disk.
/// </summary>
/// <remarks>
///     Both directions already worked — <see cref="RemoteFile" /> travels as multipart and
///     <see cref="Navigator.Download(string, Stream, string?)" /> saves a stream on every host. What was
///     missing was the join, and without it each call site grew the same four-line adapter: exactly the
///     hand-rolled plumbing this package exists to delete.
/// </remarks>
public static class FileBridgeExtensions
{
    /// <summary>
    ///     Presents a file the user picked as one a message can carry.
    /// </summary>
    /// <param name="file">The picked file, from a file input's callback.</param>
    /// <returns>A <see cref="RemoteFile" /> to assign to a message property.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file" /> is null.</exception>
    /// <remarks>
    ///     <para>
    ///         The stream is opened when the upload reads it, not here, so a picked file costs nothing
    ///         until it is actually sent — and a message built but never dispatched never touches it.
    ///     </para>
    ///     <para>
    ///         <see cref="RaskFile.OpenReadStream" /> defaults to a 512 KB ceiling, which exists to stop
    ///         an unbounded read of a browser-supplied file. Here the file's own <see cref="RaskFile.Size" />
    ///         is passed instead: the size is already known, so the ceiling can be exactly the file rather
    ///         than a guess that silently truncates anything larger. What bounds an upload is the server's
    ///         <c>MaxUploadBytes</c> — the side that has to store it.
    ///     </para>
    /// </remarks>
    public static RemoteFile AsRemote(this RaskFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return RemoteFile.FromStream(
            file.Name,
            file.ContentType,
            file.Size,
            cancellationToken => file.OpenReadStream(file.Size, cancellationToken),
            file.LastModified);
    }

    /// <summary>
    ///     Saves a file a message answered with to the user's disk.
    /// </summary>
    /// <param name="navigator">The injected <see cref="Navigator" />.</param>
    /// <param name="download">The file the handler returned.</param>
    /// <exception cref="ArgumentNullException"><paramref name="navigator" /> or <paramref name="download" /> is null.</exception>
    /// <remarks>
    ///     Must be called from inside an event handler, like every other
    ///     <see cref="Navigator.Download(string, Stream, string?)" /> call: a browser only starts a save
    ///     in response to something the user did. The stream is handed over rather than read, so a large
    ///     export never lands in memory on its way through.
    /// </remarks>
    public static void Download(this Navigator navigator, FileDownload download)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(download);

        navigator.Download(download.FileName, download.OpenReadStream(), download.ContentType);
    }
}
