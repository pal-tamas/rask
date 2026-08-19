namespace Rask.Cqrs.Server;

/// <summary>
///     Configures the endpoint pair <c>MapRaskCqrs()</c> maps. Every default here is the safe one; each
///     property is a deliberate loosening.
/// </summary>
public sealed class RaskCqrsServerOptions
{
    /// <summary>
    ///     The path the two endpoints are mapped under. The message's wire name is appended as a route
    ///     segment. Must match the client's prefix.
    /// </summary>
    public string RoutePrefix { get; set; } = RemoteEndpointDefaults.RoutePrefix;

    /// <summary>
    ///     Whether a request must come from an authenticated user. <b>True by default</b>: a message
    ///     whose author never considered authorization is rejected rather than exposed. Set it false
    ///     only for an app that genuinely has no sign-in, and reach for
    ///     <c>[AllowAnonymous]</c> on the individual handler otherwise.
    /// </summary>
    public bool RequireAuthenticatedUser { get; set; } = true;

    /// <summary>
    ///     The largest JSON request body accepted, in bytes. Enforced before any allocation
    ///     proportional to the body, so an oversized request costs nothing to reject.
    /// </summary>
    public long MaxRequestBytes { get; set; } = 1024 * 1024;

    /// <summary>
    ///     The largest multipart upload accepted, in bytes, across all of a message's files.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>The largest number of file parts one message may carry.</summary>
    public int MaxFileCount { get; set; } = 8;

    /// <summary>
    ///     Whether a handler's exception message reaches the client in the problem document.
    ///     <b>False by default</b> — an exception message is written for an operator, not for a
    ///     browser, and routinely names tables, paths and internal identifiers. Turn it on in
    ///     development only.
    /// </summary>
    public bool IncludeExceptionDetail { get; set; }

    /// <summary>
    ///     How long an opened chunked-upload session survives without a chunk before its parts are
    ///     discarded. It bounds an abandoned upload's disk, not a user's patience: every arriving chunk
    ///     refreshes it, so a slow connection is not a deadline.
    /// </summary>
    public TimeSpan UploadSessionLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    ///     The largest single chunk the upload endpoint will accept. A chunked upload exists to bound
    ///     memory, so the chunk itself has to be bounded or the mechanism defeats its own purpose.
    /// </summary>
    public long MaxUploadChunkBytes { get; set; } = 4L * 1024 * 1024;

    // Deliberately absent: a download-token lifetime. A download is fetched by the same authenticated
    // request that dispatched the query, so there is no token to expire. Naming a lifetime here would
    // describe a second, tokenized fetch path that does not exist.

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(RoutePrefix) || RoutePrefix[0] != '/')
        {
            throw new InvalidOperationException(
                $"{nameof(RoutePrefix)} must be a rooted path such as '{RemoteEndpointDefaults.RoutePrefix}'; "
                + $"got '{RoutePrefix}'.");
        }

        if (MaxRequestBytes <= 0)
        {
            throw new InvalidOperationException($"{nameof(MaxRequestBytes)} must be positive.");
        }

        if (MaxUploadBytes <= 0)
        {
            throw new InvalidOperationException($"{nameof(MaxUploadBytes)} must be positive.");
        }

        if (MaxFileCount <= 0)
        {
            throw new InvalidOperationException($"{nameof(MaxFileCount)} must be positive.");
        }

        if (UploadSessionLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(UploadSessionLifetime)} must be positive.");
        }

        if (MaxUploadChunkBytes <= 0)
        {
            throw new InvalidOperationException($"{nameof(MaxUploadChunkBytes)} must be positive.");
        }
    }
}
