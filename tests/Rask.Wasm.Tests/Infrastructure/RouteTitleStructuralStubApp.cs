using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Components.Generated;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

// Per-route <title> AND a body that restructures by route: the default path renders a Div,
// "/destination" renders an unkeyed list. The diff therefore produces untrusted positional
// structural ops, which DiffOpsAreClientSupported rejects → the nav falls back to full HTML
// (head fragment never sent). Pairs with the head-fragment test to lock that contract.
internal sealed partial class RouteTitleStructuralStubApp : Component
{
    private readonly RouteState _routeState;

    public RouteTitleStructuralStubApp(RouteState routeState) => _routeState = routeState;

    protected override Component? HeadAssets => Title[$"title-{_routeState.Path}"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        _routeState.Path == "/destination"
            ? Ul[Li["a"], Li["b"], Li["c"]]
            : Div["plain"]
    ];
}
