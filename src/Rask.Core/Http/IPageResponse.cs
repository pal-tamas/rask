namespace Rask.Core.Http;

/// <summary>
///     The HTTP response the current page is being rendered into, for the cases where the page —
///     not the route table — knows what the answer should be.
/// </summary>
/// <remarks>
///     <para>
///         The framework already answers <c>404</c> for a path that falls through to the not-found
///         page and <c>500</c> for a render that faulted. Neither can help the common case:
///         <c>/products/9999</c> matches a real route, renders a perfectly ordinary "no such
///         product" page, and tells every cache and crawler it is fine. Only the page knows.
///     </para>
///     <para>
///         Server-only in effect. On WASM there is no response to shape, so every member is a
///         no-op and a page that calls one compiles and runs unchanged on both hosts — the same
///         arrangement <c>RouteState</c> and <c>Navigator</c> use.
///     </para>
///     <para>
///         Legal only while the initial server render is running: <c>Render()</c>, <c>OnMount</c>,
///         <c>OnMountAsync</c>. Calling it from an event handler throws, because by then the
///         response is long gone and a silently dropped status is worse than a crash the developer
///         sees immediately.
///     </para>
///     <para>
///         Redirects are NOT here. <c>Navigator.NavigateTo</c> already expresses "the user belongs
///         somewhere else", and during the initial render the host turns it into a real
///         <c>302</c> — so navigation stays one concept rather than two that differ by where they
///         are called from.
///     </para>
/// </remarks>
public interface IPageResponse
{
    /// <summary>
    ///     Answer with <paramref name="statusCode" /> instead of the framework's default.
    /// </summary>
    /// <remarks>
    ///     A faulted render still wins with <c>500</c> — a page that threw does not get to claim it
    ///     succeeded. Setting <c>200</c> on the not-found page is the supported way to express a
    ///     deliberate soft-404.
    /// </remarks>
    /// <param name="statusCode">An HTTP status in the 200–599 range.</param>
    void SetStatus(int statusCode);
}
