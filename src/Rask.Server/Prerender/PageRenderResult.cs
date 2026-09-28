namespace Rask.Server.Prerender;

/// <summary>A page render, and the response it ends in.</summary>
/// <param name="Kind">Whether the page rendered or redirected.</param>
/// <param name="Html">The render, with no session id or per-response attribute stamped on it.</param>
/// <param name="RedirectLocation">
///     Where a <see cref="PageRenderKind.Redirect" /> points: sanitized to a local path and prefixed with
///     <c>PathBase</c>, ready for a <c>Location</c> header. <c>null</c> otherwise.
/// </param>
/// <param name="StatusCode">The status the page answers with. See <see cref="PageStatus.Of" />.</param>
internal readonly record struct PageRenderResult(
    PageRenderKind Kind,
    string Html,
    string? RedirectLocation,
    int StatusCode);
