using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// Like NavigateInHandlerStateHasChangedApp, but the <title> depends on the route path,
// so navigating changes the rendered <head>. Exercises the head-changed branch of the
// diff gate: a body-only diff would freeze the title, so navigation must fall back to
// full HTML. Subscribes to RouteState.Changed (same shape as the sibling fixture) so the
// root re-renders when the route flips inside the WS-handler dispatch.
public sealed partial class RouteTitleNavApp : Component
{
    private readonly RouteState _routeState;

    public RouteTitleNavApp(RouteState routeState) => _routeState = routeState;

    protected override void OnMount() => _routeState.Changed += StateHasChanged;

    protected override void OnUnmount() => _routeState.Changed -= StateHasChanged;

    protected override Component? Head => new Title()[$"t-{_routeState.Path}"];
    protected override string? HtmlLang => null;

    protected override Component? Render() => new H1()[$"path={_routeState.Path}"];
}
