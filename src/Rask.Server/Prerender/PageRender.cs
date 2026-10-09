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
        PrepareForVisitor(services, input);

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
                // Awaited, so a page that loads its data in Mount ships that data rather than its
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

        if (RedirectOf(navigator, html) is { } redirect)
        {
            return redirect;
        }

        var notFoundMounted = input.NotFoundPage is { } notFound && session.LastRenderMounted(notFound);

        return new PageRenderResult(
            PageRenderKind.Rendered,
            html,
            RedirectLocation: null,
            PageStatus.Of(session.LastRenderFaulted, pageResponse.Status, notFoundMounted));
    }

    // Where the page sent the reader as it rendered, as the response that takes them there; null when it stayed.
    private static PageRenderResult? RedirectOf(Navigator navigator, string html)
    {
        // A page of this site outside the app (Go.Out): the address as the app wrote it, with no path base in
        // front. It was checked for being local where it was given, and again here, where it becomes a header.
        if (navigator.TryConsumeExit(out var exit))
        {
            return new PageRenderResult(
                PageRenderKind.Redirect, html, LocalUrl.Sanitize(exit), StatusCodes.Status302Found);
        }

        if (!navigator.TryConsumeHistory(out var redirectUrl, out _))
        {
            return null;
        }

        return new PageRenderResult(
            PageRenderKind.Redirect,
            html,
            // Sanitized here even though NavigateTo takes a local path by contract: this value reaches
            // a Location header, and a header is exactly where an unchecked path becomes an open
            // redirect.
            LiveOptions.PathBase + LocalUrl.Sanitize(redirectUrl),
            StatusCodes.Status302Found);
    }

    // Identity, route and language, all BEFORE the first wave — so the page is built for this visitor
    // rather than rendered in the default and corrected in a second frame they would see flash.
    private static void PrepareForVisitor(IServiceProvider services, PageRenderInput input)
    {
        services.GetRequiredService<SessionUserProvider>().Set(input.User);
        var routeState = services.GetRequiredService<RouteState>();
        routeState.Path = input.Path;
        routeState.Query = input.Query;
        if (input.Culture is { } culture)
        {
            ServerCultureNegotiation.Apply(services, culture);
        }
    }
}
