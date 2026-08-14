using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Components.Generated;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

// Like StubApp, but the <title> depends on the current route path — so navigating
// changes the rendered <head>. Exercises the head-changed branch of the diff gate:
// a body-only diff would freeze the title, so navigation must fall back to full HTML.
internal sealed partial class RouteTitleStubApp : Component
{
    private readonly RouteState _routeState;

    public RouteTitleStubApp(RouteState routeState) => _routeState = routeState;

    protected override Component? HeadAssets => Title[$"title-{_routeState.Path}"];
    protected override string? HtmlLang => null;

    protected override Component? Render() => H1[$"path={_routeState.Path}"];
}
