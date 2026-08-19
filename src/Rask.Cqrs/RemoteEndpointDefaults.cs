namespace Rask.Cqrs;

/// <summary>
///     The wire constants the client and the server halves must agree on, declared once in the package
///     they both depend on.
/// </summary>
/// <remarks>
///     These are the values a drift between the two transports would break silently — a client posting
///     to a path the server never mapped, or omitting the header the server requires, produces a 404 or
///     a 400 that looks like an application bug. Defining them here makes the agreement a compile-time
///     one.
/// </remarks>
public static class RemoteEndpointDefaults
{
    /// <summary>
    ///     The default path both endpoints are mapped under. The message's wire name is appended as a
    ///     route segment, so <c>GET /_rask/cqrs/request/{name}</c> and the matching POST are the two
    ///     routes an app exposes however many messages it has.
    /// </summary>
    public const string RoutePrefix = "/_rask/cqrs/request";

    /// <summary>
    ///     The query-string parameter a GET carries its message in, as url-encoded compact JSON.
    /// </summary>
    public const string MessageQueryParameter = "m";

    /// <summary>
    ///     The header every request must carry. Its only job is CSRF: no form, <c>&lt;img&gt;</c> or
    ///     <c>&lt;script&gt;</c> can set a custom header, so neither endpoint is reachable by
    ///     cross-site markup — only by a same-origin <c>fetch</c>. Adding the GET surface therefore
    ///     adds no cross-site trigger.
    /// </summary>
    public const string RequestHeader = "X-Rask-Cqrs";

    /// <summary>The value <see cref="RequestHeader" /> carries. Only its presence is checked.</summary>
    public const string RequestHeaderValue = "1";

    /// <summary>
    ///     The url length above which the client sends a query as a POST instead of a GET. Kestrel's
    ///     request line caps near 8 KB and proxies commonly cut lower, so the fallback is deliberately
    ///     conservative: a query that works in development must not 414 behind a customer's proxy.
    /// </summary>
    public const int MaxQueryUrlLength = 2000;

    /// <summary>
    ///     The path segment appended to <see cref="RoutePrefix" /> for chunked uploads, so a large file
    ///     arrives in bounded pieces before the message that carries it is sent.
    /// </summary>
    public const string UploadSegment = "upload";

    /// <summary>The header naming the upload session a chunk belongs to, and that a message spends.</summary>
    public const string UploadHeader = "X-Rask-Upload";

    /// <summary>The header carrying which of a message's files a chunk belongs to (its wire index).</summary>
    public const string UploadFileHeader = "X-Rask-Upload-File";

    /// <summary>The header carrying the byte offset a chunk starts at.</summary>
    public const string UploadOffsetHeader = "X-Rask-Upload-Offset";

    /// <summary>
    ///     The header carrying the file's name, url-encoded. Sent with every chunk and kept from the
    ///     first, so the handler is handed a file named the way the sender named it rather than a temp
    ///     path — and so the message itself needs no sidecar describing what it uploaded.
    /// </summary>
    public const string UploadNameHeader = "X-Rask-Upload-Name";

    /// <summary>The header carrying the file's content type, url-encoded.</summary>
    public const string UploadTypeHeader = "X-Rask-Upload-Type";

    /// <summary>
    ///     The upload size above which a file is sent as a chunked session rather than one multipart
    ///     request. A browser's <c>fetch</c> reads a request body into memory before sending it, so a
    ///     single-shot upload costs its own size in the tab; chunking bounds that to one chunk.
    /// </summary>
    public const long ChunkedUploadThreshold = 4L * 1024 * 1024;

    /// <summary>The default size of each chunk in a chunked upload.</summary>
    public const long UploadChunkSize = 1024 * 1024;
}
