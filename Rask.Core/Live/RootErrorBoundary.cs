using Rask.Core.Components;
using F = Rask.Core.Components.Components;

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
    private readonly Component _inner;

    public RootErrorBoundary(Component inner) => _inner = inner;

    // Exposed so the host can forward the IRenderHandle assignment to the inner App after
    // wrapping. Without this, App.StateHasChanged() would no-op (its handle is null) until
    // the first GetOrCreate inside Render() lazily forwards the handle.
    internal Component Inner => _inner;

    protected internal override bool BypassRenderCache => true;

    protected override Component Render()
    {
        var ctx = Current
                  ?? throw new InvalidOperationException(
                      "RootErrorBoundary requires an active LiveRenderContext.");
        var inner = ctx.GetOrCreate(_inner.GetType(), _ => _inner);

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

        return F.ErrorBoundary(
            Children: new Child[] { inner },
            Fallback: (ex, _) => F.Fragment(Children:
            [
                F.Doctype(),
                F.Html(Lang: "en", Children:
                [
                    F.Head(Children:
                    [
                        F.Meta(Charset: "utf-8"),
                        F.Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
                        F.Title(Children: ["Application error"])
                    ]),
                    F.Body(Children: [new DefaultErrorPage(ex)])
                ])
            ]));
    }

    private static LiveRenderContext? Current => LiveRenderContext.Current;
}
