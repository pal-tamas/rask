using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Tags;
using static Rask.Core.Components.Components;

namespace Rask.Wasm.Tests.Infrastructure;

internal sealed class StubApp : Component
{
    private readonly RouteState _routeState;
    public int Counter;

    public StubApp(RouteState routeState) => _routeState = routeState;

    protected override Component Render() =>
        Fragment(
            Doctype(),
            Html(Children:
            [
                Head(Children: [Title(Children: ["stub"])]),
                Body(Children:
                [
                    H1(Children: [$"path={_routeState.Path}"]),
                    P(Children: [$"count={Counter}"]),
                    Button(OnClick: () => Counter++, Children: ["bump"])
                ])
            ]));
}
