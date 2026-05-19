using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Components.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

internal sealed class StubApp : Component
{
    private readonly RouteState _routeState;
    public int Counter;

    public StubApp(RouteState routeState) => _routeState = routeState;

    protected override Component Render() =>
        Fragment()[
            Doctype(),
            Html()[
                Head()[Title()["stub"]],
                Body()[
                    H1()[$"path={_routeState.Path}"],
                    P()[$"count={Counter}"],
                    Button(OnClick: () => Counter++)["bump"]
                ]
            ]];
}
