namespace Rask.Cqrs;

/// <summary>
///     A file returned <b>from</b> a message — the download direction. Declare it as a query's result
///     (<c>IQuery&lt;FileDownload&gt;</c>) and the handler answers with bytes plus the metadata a
///     browser needs to save them.
/// </summary>
/// <remarks>
///     <para>
///         The mirror of <see cref="RemoteFile" />, and transport-agnostic for the same reason. In a
///         single process the instance travels straight back to the caller. Across a server boundary
///         the transport streams it as the HTTP response body and rebuilds an equivalent instance over
///         the response stream, so nothing is buffered on either side and the handler is unchanged.
///     </para>
///     <para>
///         Named <c>FileDownload</c> rather than <c>FileResult</c> deliberately: a handler in an
///         ASP.NET project that also has <c>using Microsoft.AspNetCore.Mvc;</c> would otherwise hit
///         CS0104 on every declaration.
///     </para>
///     <para>
///         Single-consumption: the content is read exactly once, by whichever of
///         <see cref="OpenReadStream" /> or <see cref="WriteToAsync" /> is called first. A second call
///         throws rather than returning silently empty content.
///     </para>
/// </remarks>
public sealed class FileDownload
{
    private readonly Func<Stream> _open;
    private int _consumed;

    private FileDownload(string fileName, string? contentType, long? length, Func<Stream> open)
    {
        FileName = fileName;
        ContentType = string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType;
        Length = length;
        _open = open;
    }

    /// <summary>
    ///     The name suggested to the browser's save dialog. Sent as the <c>Content-Disposition</c>
    ///     filename; the transport reduces it to a safe leaf before it reaches a header.
    /// </summary>
    public string FileName { get; }

    /// <summary>The MIME type, defaulting to <c>application/octet-stream</c>.</summary>
    public string ContentType { get; }

    /// <summary>
    ///     The content length when known, so the transport can send <c>Content-Length</c> and the
    ///     browser can show real progress. Null means chunked — correct, just less informative.
    /// </summary>
    public long? Length { get; }

    /// <summary>Answers with bytes already in memory.</summary>
    /// <param name="fileName">The name suggested to the save dialog.</param>
    /// <param name="contentType">The MIME type; <c>application/octet-stream</c> when null or empty.</param>
    /// <param name="bytes">The content. Not copied — do not mutate the array afterwards.</param>
    public static FileDownload FromBytes(string fileName, string? contentType, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(bytes);

        return new FileDownload(fileName, contentType, bytes.LongLength, () => new MemoryStream(bytes, false));
    }

    /// <summary>
    ///     Answers with a stream. Ownership transfers: whoever consumes the download disposes the
    ///     stream, so a handler must not wrap this in a <c>using</c>.
    /// </summary>
    /// <param name="fileName">The name suggested to the save dialog.</param>
    /// <param name="contentType">The MIME type; <c>application/octet-stream</c> when null or empty.</param>
    /// <param name="stream">The readable content stream.</param>
    /// <param name="length">The length when known; null sends the body chunked.</param>
    public static FileDownload FromStream(string fileName, string? contentType, Stream stream, long? length = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(stream);

        return new FileDownload(fileName, contentType, length ?? KnownLength(stream), () => stream);
    }

    /// <summary>
    ///     Opens the content for reading. Call once; the caller owns and disposes the stream.
    /// </summary>
    /// <exception cref="InvalidOperationException">The content has already been consumed.</exception>
    public Stream OpenReadStream()
    {
        if (Interlocked.Exchange(ref _consumed, 1) == 1)
        {
            throw new InvalidOperationException(
                $"The content of '{FileName}' has already been read. A FileDownload carries its bytes once — "
                + "buffer them yourself if you need to read them twice.");
        }

        return _open();
    }

    /// <summary>Copies the content into <paramref name="destination" />, then disposes the source.</summary>
    /// <param name="destination">The stream to write to; not disposed.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <exception cref="InvalidOperationException">The content has already been consumed.</exception>
    public async Task WriteToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var source = OpenReadStream();
        await using (source.ConfigureAwait(false))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
    }

    // A seekable stream knows its own remaining length, which is worth reading: it turns a chunked
    // response into one with Content-Length, so the browser can render a real progress bar. A
    // non-seekable stream (a network body, a pipe) throws on both members, hence the guard rather
    // than a try/catch.
    private static long? KnownLength(Stream stream) =>
        stream.CanSeek ? stream.Length - stream.Position : null;
}
