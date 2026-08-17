using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Core.Routing;

/// <summary>
///     Matches the current URL against the app's routes and renders the page that wins. Place one near the
///     root of the app; the pages themselves are registered by their <see cref="RouteAttribute" />, so
///     there is no table to maintain.
/// </summary>
public sealed class Router : Component
{
    private readonly RouteState _state;
    private IReadOnlyList<RouteLeaf> _leaves = Array.Empty<RouteLeaf>();
    private IReadOnlyList<Route>? _routes;

    public Router(RouteState state) => _state = state;

    // Settable from the auto-generated factory. A null assignment resolves to the
    // assembly's `RouteRegistry.BuildTree()` snapshot so `Router()` (the zero-arg call
    // shape) Just Works — the generated factory passes Routes: null and the setter fills
    // in the default. The reference cache below prevents pointless re-flattening on
    // same-tree re-renders.
    /// <summary>
    ///     The route table to match against. Leave it unset — the default — and the router uses every
    ///     <c>[Route]</c>-attributed page the generator found in the entry assembly, which is what an
    ///     ordinary app wants. Supply a list only to route over a set you build yourself.
    /// </summary>
    public IReadOnlyList<Route>? Routes
    {
        get => _routes;
        set
        {
            var resolved = value ?? RouteRegistry.BuildTree();
            if (ReferenceEquals(_routes, resolved))
            {
                return;
            }

            _routes = resolved;
            _leaves = RouteFlattener.Flatten(resolved);
        }
    }

    // Render() publishes ctx.Route — per-FRAME state that the whole subtree below reads and that
    // exists only for as long as this frame's walk. The render cache breaks that: a Router whose
    // props and state are both clean is skipped, ctx.Route is never assigned, and any descendant
    // that does render (a fresh Outlet, say) reaches RouteChainRenderer with a null route and
    // throws "requires an active route context" — the whole page becomes an error boundary.
    //
    // The same reasoning covers Outlet, which advances RouteRenderState.Cursor: the cursor is
    // frame-global and positional, so the chain is only coherent if EVERY participant in the walk
    // runs on EVERY frame. Half a cached chain hands a page the wrong chain index.
    //
    // This is cheap to pay: Render() here is a route match plus a chain entry, and the page
    // components the chain resolves to are still cached normally — the expensive half is untouched.
    //
    // Not new to the chain surface, but only reachable there: the generated factory used to
    // re-apply every property each render, so nothing was ever actually render-cached and this
    // could not fire. See #682.
    protected override bool BypassRenderCache => true;

    // Subscribe to RouteState.Changed so Render() re-executes on every nav and the
    // route chain reflects the new path/query. Unsubscribe in OnUnmount.
    protected override void OnMount() => _state.Changed += StateHasChanged;

    protected override void OnUnmount() => _state.Changed -= StateHasChanged;

    protected override Component? Render()
    {
        if (!RouteMatcher.TryMatch(_leaves, _state.Path, out var chain, out var values))
        {
            return new Fragment();
        }

        var ctx = LiveRenderContext.Current
                  ?? throw new InvalidOperationException(
                      "Router() must render under a Rask live root. Call this through MapRask<TApp>.");

        ctx.Route = new RouteRenderState(_state.Path, chain, values, _state.Query);
        return RouteChainRenderer.RenderChainEntry(ctx);
    }
}
