using Rask.Core;
using Rask.Core.Routing;
using Rask.Ui;

namespace Rask.DevTools.Panel;

/// <summary>
///     What the panel shows when it opens: the inspected session's wire traffic. The tree, renders, perf and errors tabs
///     join it as they arrive.
/// </summary>
[Route("")]
[ParentRoute(typeof(DevToolsLayout))]
internal sealed partial class DevToolsOverviewPage(RouteState route, IDevToolsInspection inspection) : Component
{
    /// <inheritdoc />
    protected override Component? Render()
    {
        var session = route.Query.TryGetValue("inspect", out var inspect) ? inspect.ToString() : null;
        var token = route.Query.TryGetValue("t", out var t) ? t.ToString() : null;

        // Opened on every render rather than remembered from admission — see IDevToolsInspection. Keyed by the session,
        // so a panel that ever shows a different one mounts a fresh tab instead of re-pointing the old subscription.
        return inspection.Open(session, token) is { } feed
            ? DevToolsWireTab.Key(session).Feed(feed)
            : UiAlert["No session to inspect. Open the panel from a page's Rask pill."];
    }
}
