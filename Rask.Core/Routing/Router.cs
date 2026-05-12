using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Core.Routing;

public sealed class Router : Component
{
    private readonly RouteState _state;
    private IReadOnlyList<RouteLeaf> _leaves = Array.Empty<RouteLeaf>();
    private IReadOnlyList<Route>? _routes;

    public Router(RouteState state) => _state = state;

    // Router reads RouteState, which the framework can't observe — opt out of the render
    // cache so route changes always reach the chain.
    protected internal override bool BypassRenderCache => true;

    internal void SetRoutes(IReadOnlyList<Route> routes)
    {
        if (ReferenceEquals(_routes, routes))
        {
            return;
        }

        _routes = routes;
        _leaves = RouteFlattener.Flatten(routes);
    }

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
