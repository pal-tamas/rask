using Rask.Core;
using Rask.Core.Routing;
using Rask.Ui;

namespace Rask.DevTools.Panel;

/// <summary>
///     What the panel shows when it opens: which session it is inspecting. The tabs — wire, tree, renders, perf,
///     errors — hang off this page as they arrive.
/// </summary>
[Route("")]
[ParentRoute(typeof(DevToolsLayout))]
internal sealed partial class DevToolsOverviewPage(RouteState route) : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        route.Query.TryGetValue("inspect", out var session)
            ? UiAlert[$"Inspecting session {session}."]
            : UiAlert["No session to inspect. Open the panel from a page's Rask pill."];
}
