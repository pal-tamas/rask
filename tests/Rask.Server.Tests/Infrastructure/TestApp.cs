using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

public sealed class TestApp : Component
{
    private readonly RouteState _routeState;
    public int Counter;

    public TestApp(RouteState routeState) => _routeState = routeState;

    protected override Component? Render() =>
    [
        Doctype(),
        new Html()[new Head()[new Title()["test"]],
            new Body()[new H1()[$"path={_routeState.Path}"],
                new P()[$"count={Counter}"],
                Button(OnClick: () => Counter++)["bump"]]]
    ];
}
