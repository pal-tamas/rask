using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Components.Generated;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Native.Tests.Infrastructure;

// A minimal full-shell app for the native session tests: a counter button + the current route path, so
// tests can exercise handler dispatch (bump the counter) and navigation (assert the path). Mirrors the
// WASM host's StubApp.
internal sealed partial class NativeStubApp : Component
{
    private readonly RouteState _routeState;
    public int Counter;

    public NativeStubApp(RouteState routeState) => _routeState = routeState;

    protected override Component? Head => Title["stub"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        H1[$"path={_routeState.Path}"],
        P[$"count={Counter}"],
        Button.OnClick(() => Counter++)["bump"]
    ];
}
