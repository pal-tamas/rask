using Rask.Core;

namespace Rask.DevTools.Tests;

/// <summary>
///     An app that renders a router, like every real one. <see cref="DevToolsTestApp" /> renders its markup directly, so a
///     navigation changes nothing it shows — which cannot prove what a routed app renders at a panel path.
/// </summary>
/// <remarks>
///     The router is the default one, matching every page the registry holds — the devtools' own included — which is the
///     shape under test. The handler sits on the root rather than on a routed page: a <c>[Route]</c> page here would make
///     the generator emit a second route registry type, colliding with the one this assembly already sees in
///     Rask.DevTools through its internals.
/// </remarks>
public sealed partial class DevToolsRoutedTestApp : Component
{
    private int _clicks;

    protected override Component? Render() =>
    [
        P[$"routed clicks={_clicks}"],
        Button.OnClick(() => _clicks++)["click"],
        Router
    ];
}
