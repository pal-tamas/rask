using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Core.Routing;

public sealed class Router : Component
{
    private readonly RouteState _state;
    private IReadOnlyList<RouteLeaf> _leaves = Array.Empty<RouteLeaf>();
    private IReadOnlyList<Route>? _routes;

    // The memoised match (#688). Render() runs on EVERY frame now — it has to, because it publishes
    // ctx.Route for the whole subtree (see BypassRenderCache below) — and RouteMatcher.TryMatch allocates
    // the chain list and the values dictionary each time it is asked. It is a pure function of the
    // flattened leaves and the path, and neither changes between renders except on navigation or a
    // `Routes` reassignment, so the answer is worth keeping. Same spirit as the `Routes` setter's
    // reference cache just below, which already refuses to re-flatten the same tree.
    //
    // The results are safe to share across frames because nothing mutates them: RouteChainRenderer reads
    // Chain by index and hands Values to PageBinder, which also only reads. The per-frame part of the
    // state is RouteRenderState — its Cursor, and the Query, both of which are still taken fresh below.
    private IReadOnlyList<RouteLeaf>? _matchedLeaves;
    private string? _matchedPath;
    private IReadOnlyList<Type>? _matchedChain;
    private IReadOnlyDictionary<string, string?>? _matchedValues;
    private bool _matched;

    public Router(RouteState state) => _state = state;

    // Settable from the auto-generated factory. A null assignment resolves to the
    // assembly's `RouteRegistry.BuildTree()` snapshot so `Router()` (the zero-arg call
    // shape) Just Works — the generated factory passes Routes: null and the setter fills
    // in the default. The reference cache below prevents pointless re-flattening on
    // same-tree re-renders.
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
        var path = _state.Path;
        if (!ReferenceEquals(_matchedLeaves, _leaves)
            || !string.Equals(_matchedPath, path, StringComparison.Ordinal))
        {
            _matched = RouteMatcher.TryMatch(_leaves, path, out var chain, out var values);
            _matchedLeaves = _leaves;
            _matchedPath = path;
            _matchedChain = chain;
            _matchedValues = values;
        }

        if (!_matched)
        {
            return new Fragment();
        }

        var ctx = LiveRenderContext.Current
                  ?? throw new InvalidOperationException(
                      "Router() must render under a Rask live root. Call this through MapRask<TApp>.");

        // A fresh RouteRenderState per frame even on a memoised match: its Cursor is per-frame walk
        // state, and the Query is read now rather than when the path last changed — `?page=2` moves
        // without the path moving.
        ctx.Route = new RouteRenderState(path, _matchedChain!, _matchedValues!, _state.Query);
        return RouteChainRenderer.RenderChainEntry(ctx);
    }
}
