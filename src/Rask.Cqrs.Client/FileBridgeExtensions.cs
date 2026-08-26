using Rask.Core.Routing;

namespace Rask.Cqrs.Client;

/// <summary>
///     The download half of a file's round trip: the file a message answered with, saved to disk.
/// </summary>
/// <remarks>
///     <para>
///         There is deliberately no upload counterpart. A message declares its file as
///         <see cref="Rask.Core.Forms.RaskFile" /> — the same type a file input hands a component — so the
///         file a user picked is passed straight to the handler with no conversion at the call site:
///     </para>
///     <code>
///         await dispatcher.DispatchAsync(new AttachReceipt(orderId, picked));
///     </code>
///     <para>
///         That is identical on a server-rendered app and a WASM-hosted one. Where it runs
///         in-process the handler simply receives the picked file; where it travels, the generated codec
///         carries the bytes and hands the handler a <c>RaskFile</c> over what arrived. An adapter method
///         here would be a step the developer had to know about on some hosts and not others.
///     </para>
/// </remarks>
public static class FileBridgeExtensions
{
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
