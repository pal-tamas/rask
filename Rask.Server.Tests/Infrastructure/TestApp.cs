using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;
using static Rask.Core.Tags;

namespace Rask.Server.Tests.Infrastructure;

public sealed class TestApp : Component
{
    private readonly RouteState _routeState;
    public int Counter;

    public TestApp(RouteState routeState) => _routeState = routeState;

    public override Component Render() =>
        Fragment(
            Doctype(),
            new Html(null,
                new Head(null, new Title(null, "test")),
                new Body(null,
                    new H1(null, $"path={_routeState.Path}"),
                    new P(null, $"count={Counter}"),
                    Button(OnClick: () => Counter++, Children: ["bump"]))));
}
