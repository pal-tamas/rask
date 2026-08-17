namespace Rask.Server.Files;

/// <summary>
///     Limits on what a client may upload. These are a resource guard, not a validation step: they bound
///     what the server will accept before it reads the body, so a hostile client cannot exhaust disk or
///     memory by uploading forever.
/// </summary>
/// <remarks>
///     Nothing here says a file is <em>safe</em>. Size and count are all that is checked — a file's
///     reported name and content type come from the client and can say anything. Check the contents, and
///     never serve an upload back from a path built out of its supplied name.
/// </remarks>
public sealed class RaskUploadOptions
{
    /// <summary>
    ///     The largest single file accepted, in bytes. Default 50 MB. A larger one is rejected with
    ///     <c>413</c> before the body is read.
    /// </summary>
    public long MaxFileSize { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    ///     How many files one request may carry. Default 16. Bounds the per-request cost that
    ///     <see cref="MaxFileSize" /> alone would let a client multiply.
    /// </summary>
    public int MaxFilesPerRequest { get; set; } = 16;

    /// <summary>
    ///     Maximum cumulative bytes a single session may hold in staged uploads at once. Without it an
    ///     authenticated client can stage <see cref="MaxFileSize" /> × <see cref="MaxFilesPerRequest" />
    ///     bytes per request repeatedly and accumulate unbounded temp-file storage across requests; this
    ///     caps the running total (a request that would exceed it is rejected with <c>413</c>). Staged
    ///     bytes are released when the session ends. <c>0</c> (default) or negative disables the quota.
    /// </summary>
    public long MaxBytesPerSession { get; set; }
}
