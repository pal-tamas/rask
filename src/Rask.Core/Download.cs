using Rask.Core.Routing;

namespace Rask.Core;

/// <summary>
///     Hands the browser a file to save, from an event handler: <c>Download.File("report.csv", bytes)</c>. It travels
///     over the page's own channel and the browser starts the save when the handler returns.
/// </summary>
public static class Download
{
    /// <summary>Hands the browser <paramref name="bytes" /> to save as <paramref name="filename" />.</summary>
    /// <param name="filename">The name the browser's save dialog suggests.</param>
    /// <param name="bytes">The file.</param>
    /// <param name="contentType">Its MIME type; <c>application/octet-stream</c> when left out.</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public static void File(string filename, byte[] bytes, string? contentType = null) =>
        Navigator.RequireCurrent().Download(filename, bytes, contentType);

    /// <summary>
    ///     Hands the browser <paramref name="stream" /> to save as <paramref name="filename" />; the host reads it and
    ///     disposes of it. The one for a large file.
    /// </summary>
    /// <param name="filename">The name the browser's save dialog suggests.</param>
    /// <param name="stream">The file, readable.</param>
    /// <param name="contentType">Its MIME type; <c>application/octet-stream</c> when left out.</param>
    /// <exception cref="InvalidOperationException">Called outside an event handler.</exception>
    public static void File(string filename, Stream stream, string? contentType = null) =>
        Navigator.RequireCurrent().Download(filename, stream, contentType);
}
