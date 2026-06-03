using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Core.Routing;

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

    // Subscribe to RouteState.Changed so Render() re-executes on every nav and the
    // route chain reflects the new path/query. Unsubscribe in OnUnmount.
    protected override void OnMount() => _state.Changed += StateHasChanged;

    protected override void OnUnmount() => _state.Changed -= StateHasChanged;

    protected override RenderResult Render()
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
