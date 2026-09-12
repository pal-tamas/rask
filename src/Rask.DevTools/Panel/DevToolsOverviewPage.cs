using Rask.Core;
using Rask.Core.Routing;
using Rask.Ui;

namespace Rask.DevTools.Panel;

/// <summary>
///     The panel: the inspected session's wire traffic and its component tree. Renders, perf and errors join them as they
///     arrive.
/// </summary>
/// <remarks>
///     The tab is C# state rather than a route, because one host has no routes to spend on it: a WASM panel runs as a
///     second session inside the app's own runtime, with its own container and no navigator of its own.
/// </remarks>
[Route("")]
[ParentRoute(typeof(DevToolsLayout))]
internal sealed partial class DevToolsOverviewPage(RouteState route, IDevToolsInspection inspection) : Component
{
    private const string Wire = "wire";
    private const string Tree = "tree";

    private string _tab = Wire;

    // The selected tab is a field, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var session = route.Query.TryGetValue("inspect", out var inspect) ? inspect.ToString() : null;
        var token = route.Query.TryGetValue("t", out var t) ? t.ToString() : null;

        // Opened on every render rather than remembered from admission — see IDevToolsInspection. Keyed by the session,
        // so a panel that ever shows a different one mounts a fresh tab instead of re-pointing the old subscription.
        if (inspection.Open(session, token) is not { } feed)
        {
            return UiAlert["No session to inspect. Open the panel from a page's Rask pill."];
        }

        Component tab = _tab == Tree
            ? DevToolsTreeTab.Key(session + "-tree").Feed(feed)
            : DevToolsWireTab.Key(session + "-wire").Feed(feed);

        return Div.Class("flex flex-col gap-3")[
            Div.Role("tablist").Class("flex items-center gap-1")[
                TabButton(Wire, "Wire"),
                TabButton(Tree, "Tree")
            ],
            tab
        ];
    }

    private Component TabButton(string id, string label) =>
        UiButton
            .Key(id)
            .Size(UiSize.Sm)
            .Role("tab")
            // daisyUI's own marker, written whole: a composed class name is invisible to the kit's Tailwind scan.
            .Class(_tab == id ? "btn-active" : null)
            .Aria(new Dictionary<string, string?> { ["selected"] = _tab == id ? "true" : "false" })
            .OnClick(() => _tab = id)[label];
}
