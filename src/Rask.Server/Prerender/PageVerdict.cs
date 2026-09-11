using Microsoft.AspNetCore.Http;
using Rask.Core.Live;
using Rask.Core.Rendering;

namespace Rask.Server.Prerender;

/// <summary>
///     The decisions a page render ends in — whether the page needs a live session, and what status it
///     answers with — as functions of what the render observed.
/// </summary>
/// <remarks>
///     <para>
///         These used to be inline in the GET handler. They are here so that everything rendering a page
///         reaches the same answer from the same facts: the request serving it, and anything that renders
///         the same page to keep a copy of it. Two copies of a rule this subtle drift, and the drift would
///         surface as a page served one way and stored another.
///     </para>
///     <para>
///         The primitive overloads exist so the rules can be asserted as a truth table, without a server.
///     </para>
/// </remarks>
internal static class PageVerdict
{
    /// <summary>
    ///     Whether a rendered page needs a live session.
    /// </summary>
    /// <param name="serverInteractivity">Whether any page may become live (<c>RenderModes.ServerInteractivity</c>).</param>
    /// <param name="staticPages">
    ///     Whether a page may be served as a document at all — static pages turned on, or server
    ///     interactivity turned off.
    /// </param>
    /// <param name="declaredStatic">Whether the routed page asked to be static.</param>
    /// <param name="requiresLiveSession">Whether the render walk found anything that needs a connection.</param>
    /// <param name="faulted">Whether the root boundary rendered its error document.</param>
    /// <param name="jsPending">Whether a JavaScript call is queued waiting for a frame.</param>
    /// <param name="development">Whether the host runs in Development.</param>
    /// <remarks>
    ///     Gated as a whole on <paramref name="serverInteractivity" /> rather than folding it in as one more
    ///     reason: every other clause assumes a session is available to give, and with interactivity off
    ///     none can be — including Development, which would otherwise make every page live while you are
    ///     developing the very thing you turned off.
    /// </remarks>
    internal static bool NeedsSession(
        bool serverInteractivity,
        bool staticPages,
        bool declaredStatic,
        bool requiresLiveSession,
        bool faulted,
        bool jsPending,
        bool development) =>
        serverInteractivity
        && (!(staticPages || declaredStatic)
            || requiresLiveSession
            || faulted
            // A JS call issued from a continuation AFTER the walk is invisible to the render context, but
            // it is still queued waiting for a frame that only a socket can carry.
            || jsPending
            || development);

    /// <summary>Whether <paramref name="session" />'s last render needs a live session.</summary>
    internal static bool NeedsSession(RaskServerLimits limits, bool declaredStatic, LiveSession session) =>
        NeedsSession(
            limits.ServerInteractivity,
            StaticPagesPossible(limits),
            declaredStatic,
            session.RequiresLiveSession,
            session.LastRenderFaulted,
            session.JsInvokes.HasPending,
            LiveOptions.IsDevelopment == true);

    /// <summary>
    ///     Whether a page may be served as a document under <paramref name="limits" />: static pages are
    ///     on, or interactivity is off and every page is one.
    /// </summary>
    internal static bool StaticPagesPossible(RaskServerLimits limits) =>
        limits.StaticPages || !limits.ServerInteractivity;

    /// <summary>
    ///     Whether the routed page declares <c>[RenderMode(RenderMode.Static)]</c>.
    /// </summary>
    /// <remarks>
    ///     Honoured only from the routed page itself. Letting an arbitrary helper deep in a tree force a
    ///     whole page static would be a very quiet way to break it, and the not-found page is never
    ///     declared static, since its route is incidental.
    /// </remarks>
    internal static bool DeclaredStatic(IReadOnlyList<Type> chain, Type? notFoundPage) =>
        notFoundPage is null
        && chain.Count > 0
        && DeclaredRenderModes.Of(chain[^1]) == RenderMode.Static;

    /// <summary>
    ///     The status a rendered page answers with.
    /// </summary>
    /// <param name="faulted">Whether the root boundary rendered its error document.</param>
    /// <param name="declaredStatus">What the page set through <c>IPageResponse.SetStatus</c>, if anything.</param>
    /// <param name="notFoundMounted">Whether the render actually mounted the not-found page.</param>
    /// <remarks>
    ///     <para>
    ///         A page that threw does not get to claim it succeeded — the error document is what is being
    ///         served (#607). Below that, a page's own status wins, so a page can deliberately answer 200
    ///         where the router would say 404 (a soft 404).
    ///     </para>
    ///     <para>
    ///         The not-found status is gated on the page actually having been MOUNTED, not merely resolved:
    ///         an app that renders its root directly resolves the fallback too, and 404-ing every path such
    ///         an app serves would be a far worse lie than the one being fixed.
    ///     </para>
    /// </remarks>
    internal static int Status(bool faulted, int? declaredStatus, bool notFoundMounted) =>
        faulted
            ? StatusCodes.Status500InternalServerError
            : declaredStatus ?? (notFoundMounted ? StatusCodes.Status404NotFound : StatusCodes.Status200OK);
}
