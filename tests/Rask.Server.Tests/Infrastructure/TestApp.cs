using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Html.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

public sealed partial class TestApp : Component
{
    private readonly RouteState _routeState;
    public int Counter;

    public TestApp(RouteState routeState) => _routeState = routeState;

    protected override Component? HeadAssets => new Title()["test"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        new H1()[$"path={_routeState.Path}"],
        new P()[$"count={Counter}"],
        Button.OnClick(() => Counter++)["bump"]
    ];
}
