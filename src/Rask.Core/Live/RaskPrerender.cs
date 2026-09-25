using Rask.Core.Routing;

namespace Rask.Core.Live;


/// <summary>
///     Renders an app to a complete HTML document with no host and no browser.
/// </summary>
/// <remarks>
///     <para>
///         For an app that has no server to render it per request — a browser-WebAssembly app published
///         to a static host. Without this, the first thing every visitor and every crawler receives is
///         the boot shell: a spinner, and the word "Loading". The app's real markup does not exist until
///         several megabytes of runtime have downloaded and started.
///     </para>
///     <para>
///         What comes back is what a browser would have been sent, wrapped in the same root boundary the
///         hosts install, and driven through the same waves a server's first response uses — so a page
///         whose <c>OnMount</c> loads build-time data writes the data rather than its placeholder.
///     </para>
///     <para>
///         <b>Check <see cref="PrerenderResult.Faulted" /> and
///         <see cref="PrerenderResult.TimedOut" /> before writing anything to disk.</b> A faulted render
///         still returns perfectly ordinary HTML — that is what a root boundary is for — so a caller that
///         writes it blindly publishes an error page under the route's own name, and nothing at build
///         time would say so.
///     </para>
/// </remarks>
public static class RaskPrerender
{
    /// <summary>
    ///     Renders <paramref name="app" /> as a whole document, waiting for its async work to settle.
    /// </summary>
    /// <param name="app">The root component the host would mount.</param>
    /// <param name="services">
    ///     The app's services. Seed the route on this provider's <c>RouteState</c> before calling — which
    ///     page this renders is the caller's decision, because the caller is what holds the route table.
    /// </param>
    /// <param name="budget">
    ///     How long the waves may take in total. Prerendering can afford more than a request can: it
    ///     happens once, at build time, and nobody is waiting on a socket.
    /// </param>
    /// <param name="maxWaves">Wave cap. Defaults to <see cref="QuiescentRender.DefaultMaxWaves" />.</param>
    /// <param name="cancellationToken">
    ///     Abandons the render; see <see cref="QuiescentRender.RunAsync" />. Nothing is returned for a
    ///     cancelled render, because there is nothing about it a caller should write down.
    /// </param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
    public static async Task<PrerenderResult> RenderDocumentAsync(
        Component app,
        IServiceProvider services,
        TimeSpan budget,
        int maxWaves = QuiescentRender.DefaultMaxWaves,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(services);

        // The same wrapper Rask.Server and Rask.Wasm install: it composes the shell from the app's
        // Shell / HtmlLang / BodyClass and catches anything the subtree throws. Going through it rather
        // than reimplementing the composition is the point — this has to be what a browser gets.
        var root = new RootErrorBoundary(app) { Defaults = (RaskDocumentDefaults?)services.GetService(typeof(RaskDocumentDefaults)) };

        var render = await QuiescentRender.RunAsync(
            publishOnly => root.RenderAsLiveRoot(services, publishOnly),
            budget,
            maxWaves: maxWaves,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new PrerenderResult(
            render.Html, render.TimedOut, root.RenderedFallback, render.Waves, root.FallbackError);
    }
    /// <summary>
    ///     Reads the registered route table and splits it into what a prerender pass can and cannot do.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Call after the app's routes are registered — the generator's <c>[Route]</c> registrations
    ///         run from module initializers, so simply touching the app assembly is enough.
    ///     </para>
    ///     <para>
    ///         "Can be prerendered" is decided on the parsed segments rather than by looking for a brace
    ///         in the template. The two agree today; only one of them keeps agreeing if the template
    ///         syntax ever grows a construct that contains no brace, or a literal that contains one.
    ///     </para>
    /// </remarks>
    public static PrerenderPlan PlanRoutes()
    {
        var paths = new List<string>();
        var skipped = new List<string>();

        foreach (var leaf in RouteFlattener.Flatten(RouteRegistry.BuildTree()))
        {
            // A route with no segments at all is the root, and it is prerenderable.
            if (leaf.Pattern.Segments.All(segment => segment.Kind == SegmentKind.Literal))
            {
                paths.Add(leaf.FullTemplate);
            }
            else
            {
                skipped.Add(leaf.FullTemplate);
            }
        }

        return new PrerenderPlan(paths, skipped);
    }
}
