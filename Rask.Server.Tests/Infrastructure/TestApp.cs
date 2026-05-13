using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Server.Tests.Infrastructure;

public sealed class TestApp : Component
{
    private readonly RouteState _routeState;
    public int Counter;

    public TestApp(RouteState routeState) => _routeState = routeState;

    protected override Component Render() =>
        Fragment(
            Doctype(),
            new Html { Children = [new Head { Children = [new Title { Children = ["test"] }] },
                new Body { Children = [new H1 { Children = [$"path={_routeState.Path}"] },
                    new P { Children = [$"count={Counter}"] },
                    Button(OnClick: () => Counter++, Children: ["bump"])] }] });
}
