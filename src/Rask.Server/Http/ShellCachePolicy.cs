namespace Rask.Server.Http;

/// <summary>
///     How a page response may be cached: not at all.
/// </summary>
/// <remarks>
///     Every page response embeds its session id (<c>data-rask-root</c>), which is the de-facto bearer for
///     the WebSocket, upload and download endpoints. A response carrying one must never be stored by a
///     shared proxy, a bfcache or history, or an authenticated user's session id could be persisted and
///     replayed by another principal.
/// </remarks>
internal static class ShellCachePolicy
{
    /// <summary>The <c>Cache-Control</c> value every page response carries.</summary>
    internal const string CacheControl = "no-store, no-cache, must-revalidate, private";

    /// <summary>The <c>Pragma</c> value every page response carries, for HTTP/1.0 caches.</summary>
    internal const string Pragma = "no-cache";
}
