using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Core.Routing;

public sealed class Router : Component
{
    private readonly RouteState _state;
    private IReadOnlyList<RouteLeaf> _leaves = Array.Empty<RouteLeaf>();
    private IReadOnlyList<Route>? _routes;

    public Router(RouteState state) => _state = state;

    // Settable from the auto-generated factory. The setter caches a flattened leaves view
    // so subsequent same-reference assignments don't re-flatten the tree.
    public IReadOnlyList<Route>? Routes
    {
        get => _routes;
        set
        {
            if (ReferenceEquals(_routes, value))
            {
                return;
            }

            _routes = value;
            _leaves = value is null ? Array.Empty<RouteLeaf>() : RouteFlattener.Flatten(value);
        }
    }

    // Router reads RouteState, which the framework can't observe — opt out of the render
    // cache so route changes always reach the chain.
    protected internal override bool BypassRenderCache => true;

    protected override Component Render()
    {
        if (!RouteMatcher.TryMatch(_leaves, _state.Path, out var chain, out var values))
        {
            return new Fragment();
        }

        var ctx = LiveRenderContext.Current
                  ?? throw new InvalidOperationException(
                      "Router() must render under a Rask live root. Call this through MapRask<TApp>.");

        ctx.Route = new RouteRenderState(chain, values, _state.Query);
        return RouteChainRenderer.RenderChainEntry(ctx);
    }
}
