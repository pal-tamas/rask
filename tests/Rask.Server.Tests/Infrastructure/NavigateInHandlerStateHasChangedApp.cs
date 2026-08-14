using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// Mirrors the Rask.Example.Shared ShowcaseLayout shape: a layout component above the
// Router subscribes to RouteState.Changed in OnMount and calls StateHasChanged on
// itself when the route flips. That subscription lands inside the WS-handler
// dispatch — RouteState.Path's setter invokes Changed synchronously, which fires
// StateHasChanged while InHandlerScope=true. Pre-fix the LiveSession then eagerly
// emitted an intermediate (history-less) payload before EnforceAuthAndRenderAsync's
// final send produced the navigation payload, so the browser morphed <head> twice
// per nav and the sidebar's scoped-CSS link was momentarily orphaned. Post-fix the
// in-scope StateHasChanged just sets _pendingRenderInScope, and the dispatch's tail
// RenderAndSendCoalescingAsync emits exactly one payload.
public sealed partial class NavigateInHandlerStateHasChangedApp : Component
{
    private readonly RouteState _routeState;

    public NavigateInHandlerStateHasChangedApp(RouteState routeState) => _routeState = routeState;

    protected override void OnMount() => _routeState.Changed += StateHasChanged;

    protected override void OnUnmount() => _routeState.Changed -= StateHasChanged;

    protected override Component? HeadAssets => new Title()["nav-coalesce"];
    protected override string? HtmlLang => null;

    protected override Component? Render() => new H1()[$"path={_routeState.Path}"];
}
