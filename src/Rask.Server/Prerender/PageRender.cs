using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Globalization;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Authentication;
using Rask.Server.Http;
using QueryCollection = Rask.Core.Routing.QueryCollection;

namespace Rask.Server.Prerender;

/// <summary>What a page is rendered for.</summary>
/// <param name="Path">The route path, with any <c>PathBase</c> already removed.</param>
/// <param name="Query">The query the page sees through <c>RouteState</c>.</param>
/// <param name="User">The principal the page renders for.</param>
/// <param name="Culture">The negotiated culture, or <c>null</c> when the app configured no languages.</param>
/// <param name="Chain">The resolved route chain, outermost layout first.</param>
/// <param name="NotFoundPage">The not-found page the path fell through to, or <c>null</c>.</param>
internal readonly record struct PageRenderInput(
    string Path,
    QueryCollection Query,
    ClaimsPrincipal User,
    CultureNegotiation? Culture,
    IReadOnlyList<Type> Chain,
    Type? NotFoundPage);

/// <summary>How a page render ended.</summary>
internal enum PageRenderKind
{
    /// <summary>The page rendered a document.</summary>
    Rendered,

    /// <summary>The page navigated during its own render; the answer is a redirect, not a document.</summary>
    Redirect,
}

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

/// <summary>
///     Renders a page on a session the way the first response serves it.
/// </summary>
/// <remarks>
///     <para>
///         The GET handler's render, moved out of the handler so nothing in it touches an
///         <see cref="HttpContext" />. A request applies the result to its response.
///     </para>
///     <para>
///         The session is used as given. Creating it, admitting it against <c>MaxSessions</c> and
///         removing it stay with the caller.
///     </para>
/// </remarks>
internal static class PageRender
{
    /// <summary>
    ///     Seeds <paramref name="session" /> with <paramref name="input" /> and renders it, waiting up to
    ///     the host's quiescence budget for the work the render starts.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
    internal static async Task<PageRenderResult> RenderAsync(
        LiveSession session,
        PageRenderInput input,
        RaskServerLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(limits);

        var services = session.Services;

        // Identity, route and language, all BEFORE the first wave — so the page is built for this visitor
        // rather than rendered in the default and corrected in a second frame they would see flash.
        services.GetRequiredService<SessionUserProvider>().Set(input.User);
        var routeState = services.GetRequiredService<RouteState>();
        routeState.Path = input.Path;
        routeState.Query = input.Query;
        if (input.Culture is { } culture)
        {
            ServerCultureNegotiation.Apply(services, culture);
        }

        // The page may shape the response only while this render is running. Closed in the finally so a
        // later event handler — which runs long after these bytes are gone — throws instead of setting a
        // status nobody will ever read.
        var pageResponse = services.GetRequiredService<ServerPageResponse>();
        var navigator = services.GetRequiredService<Navigator>();

        pageResponse.Phase = PageResponsePhase.Initial;
        string html;

        // Navigation is legal for the duration of this render and becomes a real redirect, so a page can
        // decide on load that the user belongs elsewhere using the same NavigateTo it would call from a
        // handler. Closed straight after, so a background render cannot navigate a request that no longer
        // exists.
        using (navigator.EnterInitialRender())
        {
            try
            {
                // Awaited, so a page that loads its data in OnMountAsync ships that data rather than its
                // placeholder. Falls back to the synchronous render when the budget is zero.
                html = await session
                    .RenderInitialRootAsync(limits.InitialRenderQuiescenceTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                pageResponse.Phase = PageResponsePhase.None;
            }
        }

        if (navigator.TryConsumeHistory(out var redirectUrl, out _))
        {
            return new PageRenderResult(
                PageRenderKind.Redirect,
                html,
                // Sanitized here even though NavigateTo takes a local path by contract: this value reaches
                // a Location header, and a header is exactly where an unchecked path becomes an open
                // redirect.
                LiveOptions.PathBase + LocalUrl.Sanitize(redirectUrl),
                StatusCodes.Status302Found);
        }

        var notFoundMounted = input.NotFoundPage is { } notFound && session.LastRenderMounted(notFound);

        return new PageRenderResult(
            PageRenderKind.Rendered,
            html,
            RedirectLocation: null,
            PageStatus.Of(session.LastRenderFaulted, pageResponse.Status, notFoundMounted));
    }
}

/// <summary>The status a rendered page answers with.</summary>
internal static class PageStatus
{
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
    internal static int Of(bool faulted, int? declaredStatus, bool notFoundMounted) =>
        faulted
            ? StatusCodes.Status500InternalServerError
            : declaredStatus ?? (notFoundMounted ? StatusCodes.Status404NotFound : StatusCodes.Status200OK);
}
