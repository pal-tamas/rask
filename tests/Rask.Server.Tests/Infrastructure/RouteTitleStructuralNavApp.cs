using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// Per-route <title> AND a body that restructures by route: most paths render a Div, the
// "/destination" path renders an unkeyed list. The diff produces untrusted positional
// structural ops, which DiffOpsAreClientSupported rejects → the nav falls back to full HTML
// (head fragment never sent). Subscribes to RouteState.Changed so the root re-renders when
// the route flips inside the WS-handler dispatch (same shape as RouteTitleNavApp).
public sealed class RouteTitleStructuralNavApp : Component
{
    private readonly RouteState _routeState;

    public RouteTitleStructuralNavApp(RouteState routeState) => _routeState = routeState;

    protected override void OnMount() => _routeState.Changed += StateHasChanged;

    protected override void OnUnmount() => _routeState.Changed -= StateHasChanged;

    protected override RenderResult Render() =>
        [
            Doctype(),
            new Html()[
                new Head()[new Title()[$"t-{_routeState.Path}"]],
                new Body()[
                    _routeState.Path == "/destination"
                        ? new Ul()[new Li()["a"], new Li()["b"], new Li()["c"]]
                        : new Div()["plain"]
                ]
            ]
        ];
}
