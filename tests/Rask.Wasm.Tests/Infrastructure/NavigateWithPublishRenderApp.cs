using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

// Reproduces the LiveTicker shape: a click handler calls Navigator.NavigateTo
// AND a child component's render calls StateHasChanged() inside the dispatch.
// That second call lands in WasmLiveSession.RequestRenderInternalAsync while
// InHandlerScope=true, sets _pendingRenderInScope, and forces
// BuildPayloadCoalescingRerendersAsync to rebuild the payload — the rebuild
// path the bug fix is about.
internal sealed partial class NavigateWithPublishRenderApp : Component
{
    private readonly Navigator _nav;
    private readonly RouteState _routeState;

    public NavigateWithPublishRenderApp(RouteState routeState, Navigator nav)
    {
        _routeState = routeState;
        _nav = nav;
    }

    protected override Component? HeadAssets => Title["nav-pub"];
    protected override string? HtmlLang => null;

    protected override Component? Render()
    {
        // Always request a render — RequestRenderInternalAsync short-circuits
        // via InHandlerScope by setting _pendingRenderInScope=true, exactly
        // the shape LiveTicker.OnRenderedAsync's auto-rerender continuation
        // produces under the framework's "publish render after every awaited
        // OnRenderedAsync" mechanism. The rebuild loop in
        // BuildPayloadCoalescingRerendersAsync is budgeted at 2 retries, so
        // unconditional StateHasChanged here doesn't spin — it just guarantees
        // the rebuild path runs and observes whether the rebuild preserves
        // the navigation entry.
        StateHasChanged();

        return
        [
            Div[$"path={_routeState.Path}"],
            Button.OnClick(() => _nav.NavigateTo("/destination"))["go"]
        ];
    }
}
