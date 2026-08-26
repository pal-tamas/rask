using Rask.Core.Components;

namespace Rask.Core.Live;

// The document root installed by Rask.Server.UseRask<TApp> and Rask.Wasm.WasmHostBuilder. Two jobs:
//
//  1. It COMPOSES THE PAGE SHELL around the App. The App renders straight into <body>; the doctype,
//     <html>, <head> and <body> are the framework's, built here from the App's own Shell/HtmlLang/
//     BodyClass hooks. Nothing about the shell depends on the App remembering to render it, which is
//     what let the old contract fail at runtime (a missing <body> left the runtime <script> nowhere to
//     land) and is why the shell-token scan and RASK021's runtime backstop are gone.
//  2. It is the root ERROR BOUNDARY: a render-time, async-lifecycle or event-handler exception
//     anywhere in the App's subtree produces a styled fallback instead of an HTTP 500 / blank screen.
//     The boundary sits INSIDE <body>, so a fault now replaces the app's content and keeps the
//     document — the head assets, the <html> attributes and the body class all survive it.
//
// The user's App becomes a framework-tracked child of this wrapper via GetOrCreate, which
// is what lets RenderAsLiveRoot's CollectAlive pass still walk through it for disposal and
// post-render lifecycle (OnRendered, etc.).
internal sealed class RootErrorBoundary : Component
{
    public RootErrorBoundary(Component inner) => Inner = inner;

    // Exposed so the host can forward the IRenderHandle assignment to the inner App after
    // wrapping. Without this, App.StateHasChanged() would no-op (its handle is null) until
    // the first GetOrCreate inside Render() lazily forwards the handle.
    internal Component Inner { get; }

    /// <summary>
    ///     Whether the most recent render walk ended up showing the fallback rather than the app.
    /// </summary>
    /// <remarks>
    ///     The boundary catches the exception, so nothing escapes <c>RenderAsLiveRoot</c> and the caller
    ///     cannot otherwise tell a crashed page from a healthy one — which is why the initial GET for a
    ///     page that threw used to answer <c>200 OK</c> (#607). Set from inside the fallback delegate,
    ///     which runs exactly when the fallback is rendered, and cleared at the top of each
    ///     <see cref="Render" /> — the delegate is invoked by the serializer walking the tree this method
    ///     returns, so the clear always precedes the set.
    /// </remarks>
    internal bool RenderedFallback { get; private set; }

    protected override bool BypassRenderCache => true;

    private static LiveRenderContext? Current => LiveRenderContext.Current;

    protected override Component? Render()
    {
        // An internal type in the message helps nobody: if this fires, the reader wrote none of the
        // words in it. Say what they did instead — rendered a component outside a live render.
        var ctx = Current
                  ?? throw new InvalidOperationException(
                      "A Rask app was rendered outside a live render context, so the framework's root "
                      + "error boundary has nothing to wrap. Render through the host — UseRask<TApp>() "
                      + "on the server, the WASM host builder in the browser — or, in a test, through "
                      + "RaskTest.Render, which sets one up for you.");
        var inner = ctx.GetOrCreate(Inner.GetType(), _ => Inner);

        // Propagate the "force the root to re-execute this frame" contract that
        // RenderAsLiveRootCore applies to its own root. Without this the inner App would
        // serve its cached render whenever no descendant marked itself dirty, missing
        // changes to externally-observed state (e.g. IUserProvider.Current after a WS
        // reconnect's Set(user); RouteState mutations are already covered by Router's
        // own BypassRenderCache).
        inner.MarkDirtyForFrame();

        // App used to be the live root, where RenderAsLiveRootCore fires lifecycle directly
        // via RaiseLifecycleBeforeRender(false). Now that the wrapper is the root, the App
        // is a child — NotifyParameters here replicates the same call so OnMount /
        // OnPropsChanged still fire on the App exactly as they used to.
        ctx.NotifyParameters(inner, false);

        // The second parameter is the boundary's Recover, and it used to be discarded. Forwarding it
        // gives the page a "Try again" that clears the error and re-renders in place — worth having
        // because the common fault is a handler that threw rather than a render that cannot succeed, so
        // the tree is intact and recovering keeps the session, the state and the scroll position that a
        // reload throws away.
        // The boundary is resolved explicitly rather than through the generated factory because the
        // fallback needs to ask it HOW it was tripped (see below), and the factory's delegate only
        // receives the exception and Recover. Same positional identity either way: GetOrCreate keys on
        // (type, position), and this is the only ErrorBoundary this method resolves — the shell
        // elements below are counted per type, so adding them cannot move it.
        var boundary = (ErrorBoundary)ctx.GetOrCreate(typeof(ErrorBoundary), static _ => new ErrorBoundary());

        RenderedFallback = false;
        boundary.SetProps([inner], (ex, recover) =>
        {
            // In development, a fault the tree survived is shown OVER the app instead of replacing it.
            //
            // The full-document swap is right in production and wrong in development, where a handler
            // that throws is the common case rather than the exceptional one: it takes the scroll
            // position, the form input, the expanded panels and the route with it, so the developer
            // loses the state that produced the bug at the moment they most want to look at it. React's
            // and Next's dev overlays leave the app mounted for exactly this reason.
            //
            // Only for a fault the tree SURVIVED. After a render fault, re-rendering the subtree that
            // just threw would only throw again — so a render fault still replaces the page, in
            // development as in production, which is the honest outcome.
            if (boundary is { Source: not ErrorSource.Render }
                && DevErrorInfo.From(ex, boundary.Source == ErrorSource.Action ? "handler" : "lifecycle")
                    is { } devError)
            {
                ctx.ReportDevError(devError);

                // Clear without asking for a render: this render is already in flight and is about to
                // show the app. Recover() would signal one, and the signal would land while the walk
                // that set it is still running.
                boundary.ClearErrorInRender();
                return inner;
            }

            RenderedFallback = true;

            // Body content only — the document around it is this component's, and it is already built
            // by the time the boundary trips. OwnsDocument lets the page contribute its own <title> and
            // the charset/viewport meta, so it is complete even when the App threw before contributing
            // any head of its own; a nested boundary's fallback replaces one widget and says nothing
            // about the document, which is why the flag exists rather than being unconditional.
            return new DefaultErrorPage(ex, recover) { OwnsDocument = true };
        });

        // Compose the document. The App's Shell override is user code that builds components, so it is
        // held to the same promise as its Render(): a throw shows the error page rather than escaping to
        // the host as a 500. The framework's own default shell takes over for that render — a custom
        // shell cannot be trusted after it has just failed, and the error page needs a document to live
        // in.
        var head = Head;
        Component document;
        try
        {
            document = inner.ShellInternal(head, boundary);
        }
        catch (Exception ex)
        {
            boundary.TripInRender(ex);
            document = ShellInternal(head, boundary);
        }

        // A collection expression, not F.Fragment(): the factory would make the wrapper a tracked child
        // and retain it, and this one is pure grouping — two children that never change, on the one
        // component that re-renders every frame. Retained per live session it measures; allocated per
        // render it does not (it is the same transient Fragment every root render used to build for the
        // App's own [Doctype(), Html(...)]).
        return [CoreDoctype, document];
    }
}
