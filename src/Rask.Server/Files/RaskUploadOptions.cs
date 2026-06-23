namespace Rask.Server.Files;

public sealed class RaskUploadOptions
{
    public long MaxFileSize { get; set; } = 50 * 1024 * 1024;

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
