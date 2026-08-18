namespace Rask.Cqrs;

/// <summary>
///     A file carried <b>by</b> a message — the upload direction. Declare it as a property on a
///     query or command and the file travels with the request; the handler receives a
///     <see cref="RemoteFile" /> it can read like any other stream.
/// </summary>
/// <remarks>
///     <para>
///         This type exists so a message stays transport-agnostic. When the handler runs in the same
///         process the instance the caller built is handed straight to it and nothing is copied. When
///         the handler runs on a server, the transport encodes the message as multipart — the scalar
///         properties as JSON, each <see cref="RemoteFile" /> as its own binary part — and rebuilds an
///         equivalent instance over the received bytes on the far side. <b>The handler cannot tell the
///         two apart</b>, which is the whole point: one handler, both hosts.
///     </para>
///     <para>
///         A <see cref="RemoteFile" /> is single-consumption by contract:
///         <see cref="OpenReadStream" /> may be called once, and only while the handler is on the
///         stack. Read what you need before returning — the underlying stream is a network body or a
///         staged temp file, and both are released when dispatch completes.
///     </para>
/// </remarks>
public abstract class RemoteFile
{
    /// <summary>The value of <see cref="Size" /> when the length is not known ahead of the read.</summary>
    public const long UnknownSize = -1;

    /// <summary>
    ///     The file's name as the client supplied it. <b>Untrusted</b> — it may contain directory
    ///     separators, traversal segments or characters the local filesystem rejects. Reduce it to a
    ///     safe leaf before using it in a path.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    ///     The MIME type the client reported, or <c>application/octet-stream</c> when it reported
    ///     none. Also untrusted: it describes what the client <em>claims</em>, not what the bytes are.
    /// </summary>
    public abstract string ContentType { get; }

    /// <summary>
    ///     The length in bytes, or <see cref="UnknownSize" /> when the transport cannot know it ahead
    ///     of reading. Treat a known size as a hint for allocation, never as a guarantee of what
    ///     <see cref="OpenReadStream" /> will actually yield.
    /// </summary>
    public abstract long Size { get; }

    /// <summary>The client-reported last-modified timestamp, when one was supplied.</summary>
    public virtual DateTimeOffset? LastModified => null;

    /// <summary>
    ///     Opens the file's bytes for reading. Call once, and only while the handler is running.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read; pass the handler's token.</param>
    /// <returns>A readable stream the caller owns and should dispose.</returns>
    public abstract Stream OpenReadStream(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Builds a <see cref="RemoteFile" /> over any stream source — the seam a host uses to adapt
    ///     its own file type (a picked browser file, a staged upload, a <c>FileInfo</c>) without this
    ///     package needing to know about it.
    /// </summary>
    /// <param name="name">The file name to send.</param>
    /// <param name="contentType">The MIME type to send; <c>application/octet-stream</c> when null or empty.</param>
    /// <param name="size">The length in bytes, or <see cref="UnknownSize" />.</param>
    /// <param name="openReadStream">Opens the bytes. Invoked at most once, when the file is read.</param>
    /// <param name="lastModified">An optional last-modified timestamp to carry along.</param>
    public static RemoteFile FromStream(
        string name,
        string? contentType,
        long size,
        Func<CancellationToken, Stream> openReadStream,
        DateTimeOffset? lastModified = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(openReadStream);

        return new DelegateRemoteFile(name, contentType, size, openReadStream, lastModified);
    }

    /// <summary>Builds a <see cref="RemoteFile" /> over bytes already in memory.</summary>
    /// <param name="name">The file name to send.</param>
    /// <param name="contentType">The MIME type to send; <c>application/octet-stream</c> when null or empty.</param>
    /// <param name="bytes">The file contents. Not copied — do not mutate the array afterwards.</param>
    /// <param name="lastModified">An optional last-modified timestamp to carry along.</param>
    public static RemoteFile FromBytes(
        string name,
        string? contentType,
        byte[] bytes,
        DateTimeOffset? lastModified = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return FromStream(name, contentType, bytes.LongLength, _ => new MemoryStream(bytes, false), lastModified);
    }

    private sealed class DelegateRemoteFile(
        string name,
        string? contentType,
        long size,
        Func<CancellationToken, Stream> openReadStream,
        DateTimeOffset? lastModified) : RemoteFile
    {
        public override string Name { get; } = name;

        public override string ContentType { get; } =
            string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType;

        public override long Size { get; } = size < 0 ? UnknownSize : size;

        public override DateTimeOffset? LastModified { get; } = lastModified;

        public override Stream OpenReadStream(CancellationToken cancellationToken = default) =>
            openReadStream(cancellationToken);
    }
}
