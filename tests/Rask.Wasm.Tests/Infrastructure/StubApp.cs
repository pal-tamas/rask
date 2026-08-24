using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

internal sealed partial class StubApp : Component
{
    private readonly RouteState _routeState;
    public int Counter;

    public StubApp(RouteState routeState) => _routeState = routeState;

    protected override Component? HeadAssets => Title["stub"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        H1[$"path={_routeState.Path}"],
        P[$"count={Counter}"],
        Button.OnClick(() => Counter++)["bump"]
    ];
}
