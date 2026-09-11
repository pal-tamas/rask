using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
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

/// <summary>A page render, and every decision it ended in.</summary>
/// <param name="Kind">Whether the page rendered or redirected.</param>
/// <param name="Html">The render, with no session id or per-response attribute stamped on it.</param>
/// <param name="RedirectLocation">
///     Where a <see cref="PageRenderKind.Redirect" /> points: sanitized to a local path and prefixed with
///     <c>PathBase</c>, ready for a <c>Location</c> header. <c>null</c> otherwise.
/// </param>
/// <param name="StatusCode">The status the page answers with. See <see cref="PageVerdict.Status" />.</param>
/// <param name="NeedsSession">Whether the page needs a live session. See <see cref="PageVerdict.NeedsSession(RaskServerLimits, bool, LiveSession)" />.</param>
/// <param name="DeclaredStatic">Whether the routed page asked to be static.</param>
/// <param name="Faulted">Whether the root boundary rendered its error document.</param>
/// <param name="TimedOut">Whether the render was served before its async work settled.</param>
/// <param name="BlockedOnJs">Whether the render stopped waiting because its work was blocked on JavaScript.</param>
/// <param name="Authenticated">
///     Whether a signed-in principal is involved — the one the page rendered for, or one the render
///     itself signed in.
/// </param>
/// <param name="ReadsUser">
///     Whether the render's markup can depend on who it was rendered for: it read the current user
///     through the framework's <c>IUserProvider</c>, or the app replaced that provider with one whose
///     reads cannot be seen, in which case it is assumed to. A read that bypasses <c>IUserProvider</c>
///     entirely — straight off the request through <c>IHttpContextAccessor</c> — is not visible here.
/// </param>
internal readonly record struct PageRenderResult(
    PageRenderKind Kind,
    string Html,
    string? RedirectLocation,
    int StatusCode,
    bool NeedsSession,
    bool DeclaredStatic,
    bool Faulted,
    bool TimedOut,
    bool BlockedOnJs,
    bool Authenticated,
    bool ReadsUser);

/// <summary>
///     Renders a page on a session the way the first response serves it.
/// </summary>
/// <remarks>
///     <para>
///         The GET handler's render, moved out of the handler so it is not the only thing that can do
///         it. Everything a response needs to decide is returned; nothing here touches an
///         <see cref="HttpContext" />. A request applies the result to its response — and a page rendered
///         for any other reason gets exactly the markup and the verdict that request would have got.
///     </para>
///     <para>
///         The session is used as given. Creating it, admitting it against <c>MaxSessions</c> and
///         discarding it stay with the caller, because those depend on why the page is being rendered.
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
        var users = services.GetRequiredService<SessionUserProvider>();
        users.Set(input.User);
        var routeState = services.GetRequiredService<RouteState>();
        routeState.Path = input.Path;
        routeState.Query = input.Query;
        if (input.Culture is { } culture)
        {
            ServerCultureNegotiation.Apply(services, culture);
        }

        // The read count sees the framework's own provider and nothing else. An app that registered an
        // IUserProvider of its own has put a door in front of it the count cannot see through — so a
        // render there is assumed to read the user rather than trusted not to. Assuming wrongly costs a
        // page that could have been shared; trusting wrongly would share one visitor's page with others.
        var userReadsAreCounted = ReferenceEquals(services.GetService<IUserProvider>(), users);

        // The page may shape the response only while this render is running. Closed in the finally so a
        // later event handler — which runs long after these bytes are gone — throws instead of setting a
        // status nobody will ever read.
        var pageResponse = services.GetRequiredService<ServerPageResponse>();
        var navigator = services.GetRequiredService<Navigator>();

        // Counted across the render and nothing else. Seeding the principal above writes the field, and
        // the authenticated check below reads it — neither is the page asking who the user is.
        var readsBefore = users.ReadCount;
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

        var readsUser = !userReadsAreCounted || users.ReadCount != readsBefore;
        var faulted = session.LastRenderFaulted;
        var authenticated = input.User.Identity?.IsAuthenticated == true
                            // The union, because a render can sign someone in.
                            || users.Current.Identity?.IsAuthenticated == true;

        if (navigator.TryConsumeHistory(out var redirectUrl, out _))
        {
            return new PageRenderResult(
                PageRenderKind.Redirect,
                html,
                // Sanitized here even though NavigateTo takes a local path by contract: this value reaches
                // a Location header, and a header is exactly where an unchecked path becomes an open
                // redirect.
                LiveOptions.PathBase + LocalUrl.Sanitize(redirectUrl),
                StatusCodes.Status302Found,
                NeedsSession: false,
                DeclaredStatic: false,
                faulted,
                session.LastRenderTimedOut,
                session.LastRenderBlockedOnJs,
                authenticated,
                readsUser);
        }

        var declaredStatic = PageVerdict.DeclaredStatic(input.Chain, input.NotFoundPage);
        var notFoundMounted = input.NotFoundPage is { } notFound && session.LastRenderMounted(notFound);

        return new PageRenderResult(
            PageRenderKind.Rendered,
            html,
            RedirectLocation: null,
            PageVerdict.Status(faulted, pageResponse.Status, notFoundMounted),
            PageVerdict.NeedsSession(limits, declaredStatic, session),
            declaredStatic,
            faulted,
            session.LastRenderTimedOut,
            session.LastRenderBlockedOnJs,
            authenticated,
            readsUser);
    }
}
