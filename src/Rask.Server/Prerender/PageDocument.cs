using Rask.Core.Live;

namespace Rask.Server.Prerender;

/// <summary>
///     Shapes a page render into the bytes a response carries.
/// </summary>
/// <remarks>
///     The one place the per-response attributes are stamped onto a render, so their order — and every
///     test asserting it — cannot differ between the paths that serve a page.
/// </remarks>
internal static class PageDocument
{
    /// <summary>
    ///     A live document: <paramref name="html" /> carrying its session id, the browser bundle's boot
    ///     module when the rung is on, and the development attributes when <paramref name="dev" />.
    /// </summary>
    /// <remarks>
    ///     <c>data-rask-dev</c> is the client-side gate for every dev-only frame, so it is decided by the
    ///     caller from the same predicate that decides whether to subscribe at all. Where to ask about
    ///     build status when the socket drops (#603) is read from the environment, because the only thing
    ///     that can answer is <c>rask dev</c>, the process that launched this one.
    /// </remarks>
    internal static string Live(string html, string sessionId, RaskServerLimits limits, bool dev) =>
        LivePayload.InjectIslandsDevAttr(
            LivePayload.InjectWasmBundleAttr(
                LivePayload.InjectRootAttr(
                    html, sessionId, dev, dev ? Environment.GetEnvironmentVariable("RASK_DEV_STATUS") : null),
                RaskEndpointExtensions.WasmBootModuleUrl(limits)),
            dev,
            dev ? Environment.GetEnvironmentVariable("RASK_ISLANDS_DEV") : null);

    /// <summary>
    ///     A render shaped to be stored and served to anyone: the bytes a live request is sent for a page
    ///     that needs no session — no session id, and no runtime script. <c>null</c> when the page needs a
    ///     live session, or when its runtime script is not exactly where it belongs.
    /// </summary>
    /// <remarks>
    ///     A page that needs a session is not stored yet: served without one it would sit on screen with
    ///     nothing to answer its handlers, which changes once the socket can take a stored page over. The
    ///     splice fails closed for the reason the live request's does — a document that might still carry a
    ///     session-bearing script is the one thing never worth sharing.
    /// </remarks>
    /// <param name="render">The neutral render of a public page.</param>
    /// <param name="pathBase">
    ///     The prefix the runtime script's URL was rendered with — <c>LiveOptions.PathBase</c> at the call
    ///     site. Passed in rather than read here, so the splice matches the render it is given.
    /// </param>
    internal static string? Baked(in PageRenderResult render, string pathBase) =>
        render.NeedsSession ? null : Http.RuntimeScriptSplice.TryRemove(render.Html, pathBase);
}
