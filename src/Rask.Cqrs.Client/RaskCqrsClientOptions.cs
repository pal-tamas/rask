using Rask.Wire;

namespace Rask.Cqrs.Client;

/// <summary>
///     Configures how the client sends a message the local process has no handler for.
/// </summary>
public sealed class RaskCqrsClientOptions
{
    /// <summary>
    ///     The origin to send to. Null — the default — means the app's own origin, which is what a
    ///     browser-hosted app wants: the request is same-origin, so the session cookie rides it and no
    ///     CORS preflight is involved. A client served from another origin sets this to its server's
    ///     absolute address.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    ///     The path the endpoints are mapped under. Must match the server's prefix.
    /// </summary>
    public string RoutePrefix { get; set; } = RemoteEndpointDefaults.RoutePrefix;

    /// <summary>
    ///     The url length above which a query is sent as a POST rather than a GET. The fallback is
    ///     automatic and produces an identical result; it exists because a url has a ceiling that
    ///     differs per proxy, and a query that 414s only in production is the worst way to discover it.
    /// </summary>
    public int MaxQueryUrlLength { get; set; } = RemoteEndpointDefaults.MaxQueryUrlLength;

    /// <summary>
    ///     The file size above which a message's files are uploaded in chunks before the message is sent,
    ///     rather than riding along as one multipart body.
    /// </summary>
    /// <remarks>
    ///     A browser's <c>fetch</c> reads a request body into memory before sending it, so a single-shot
    ///     upload costs its own size in the tab. Chunking bounds that to one chunk. A file whose size is
    ///     unknown (<see cref="RemoteFile.UnknownSize" />) is always chunked — an unknown size cannot be
    ///     compared against a threshold, and guessing "small" is the expensive way to be wrong.
    /// </remarks>
    public long ChunkedUploadThreshold { get; set; } = RemoteEndpointDefaults.ChunkedUploadThreshold;

    /// <summary>The size of each chunk. Bounds what the client holds and what one request carries.</summary>
    public long UploadChunkSize { get; set; } = RemoteEndpointDefaults.UploadChunkSize;

    /// <summary>
    ///     Runs before every request, to attach whatever proves who is calling.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A browser-hosted app usually needs nothing here: the request is same-origin, so the session
    ///         cookie rides it automatically and the server sees the signed-in user without being asked.
    ///     </para>
    ///     <para>
    ///         An app talking to <b>another origin</b> has no such ambient credential — nothing attaches
    ///         itself across origins. This is where it puts its bearer token, and the same hook covers an
    ///         app that authenticates with a token rather than a cookie.
    ///     </para>
    ///     <para>
    ///         It receives the outgoing request rather than an <c>HttpClient</c>, deliberately: attaching a
    ///         header is the thing apps actually need, and handing out the client would put back the
    ///         surface this package exists to remove.
    ///     </para>
    /// </remarks>
    public Func<HttpRequestMessage, CancellationToken, Task>? ConfigureRequestAsync { get; set; }


    /// <summary>
    ///     How long to wait for a response before abandoning the request. Applies per attempt, and is
    ///     independent of the caller's <c>CancellationToken</c> — which still cancels immediately, so a
    ///     component that unmounts mid-request aborts it rather than waiting this out.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(RoutePrefix) || RoutePrefix[0] != '/')
        {
            throw new InvalidOperationException(
                $"{nameof(RoutePrefix)} must be a rooted path such as '{RemoteEndpointDefaults.RoutePrefix}'; "
                + $"got '{RoutePrefix}'.");
        }

        if (MaxQueryUrlLength <= 0)
        {
            throw new InvalidOperationException($"{nameof(MaxQueryUrlLength)} must be positive.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(Timeout)} must be positive.");
        }

        if (ChunkedUploadThreshold <= 0)
        {
            throw new InvalidOperationException($"{nameof(ChunkedUploadThreshold)} must be positive.");
        }

        if (UploadChunkSize is <= 0 or > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{nameof(UploadChunkSize)} must be positive and fit in a single buffer.");
        }
    }
}
