using Rask.Core.Components;
using F = Rask.Core.Components.Generated;

namespace Rask.Core.Live;

// Transparent wrapper installed by Rask.Server.UseRask<TApp> and Rask.Wasm.WasmHostBuilder
// so that a render-time, async-lifecycle, or event-handler exception anywhere in the App's
// subtree produces a styled fallback page instead of an HTTP 500 / blank screen.
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
        RenderedFallback = false;
        return F.ErrorBoundary((ex, recover) =>
        {
            RenderedFallback = true;
            return F.Fragment()[
                F.Doctype(),
                F.Html("en")[
                    F.Head()[
                        F.Meta("utf-8"),
                        F.Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
                        F.Title()["Application error"]
                    ],
                    F.Body()[new DefaultErrorPage(ex, recover)]
                ]
            ];
        })[inner];
    }
}
