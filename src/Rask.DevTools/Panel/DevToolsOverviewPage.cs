using System.Runtime.InteropServices;
using Rask;
using Rask.Core;
using Rask.Core.Routing;

namespace Rask.DevTools.Panel;

/// <summary>
///     The panel: the inspected session's wire traffic, its component tree, what rendered, where the time went and what went
///     wrong.
/// </summary>
/// <remarks>
///     The tab is C# state rather than a route, because one host has no routes to spend on it: a WASM panel runs as a
///     second session inside the app's own runtime, with its own container and no navigator of its own.
/// </remarks>
[Route("")]
[ParentRoute(typeof(DevToolsLayout))]
internal sealed partial class DevToolsOverviewPage(RouteState route, IDevToolsInspection inspection) : Component
{
    private string _tab = DevToolsTabIds.Wire;

    // A component the Errors tab asked to see: the Tree tab opens the way to it and selects it. Cleared when the developer
    // picks a tab themselves, so going back to the tree later does not jump to it again.
    private long? _reveal;

    // Whether the page flashes renders: here rather than in the Renders tab, because the page keeps flashing while another
    // tab is showing, and the emitter that tells it what to flash is rendered whichever tab that is.
    private bool _flash;

    // The panel's script, on a host whose panel is a page: it tells the page that framed the panel what a row is over,
    // and hands a pick back. Deferred, so it binds to the document the page was served with.
    /// <inheritdoc />
    protected override Component? HeadAssets =>
        inspection.PanelScriptUrl is { } script ? Script.Src(script).Defer(true) : null;

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
            return Ui.Alert["No session to inspect. Open the panel from a page's Rask pill."];
        }

        Component tab = _tab switch
        {
            DevToolsTabIds.Tree => DevToolsTreeTab.Key(session + "-tree").Feed(feed).Reveal(_reveal),
            DevToolsTabIds.Renders => DevToolsRendersTab.Key(session + "-renders").Feed(feed).Flash(_flash)
                .OnFlashChange(on => _flash = on),
            DevToolsTabIds.Perf => DevToolsPerfTab.Key(session + "-perf").Feed(feed),
            DevToolsTabIds.Errors => DevToolsErrorsTab.Key(session + "-errors")
                .PageErrors(feed.Errors)
                .AppErrors(inspection.AppWide)
                .ReportEnvironment(new Probe.DevToolsBugReport.Environment(
                    inspection.HostName, RaskVersion.Current, RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.OSDescription, feed.Browser))
                .OnShowInTree(id =>
                {
                    _reveal = id;
                    _tab = DevToolsTabIds.Tree;
                }),
            _ => DevToolsWireTab.Key(session + "-wire").Feed(feed),
        };

        return Div.Class("flex flex-col gap-3")[
            // Whichever tab is showing, so the Tree tab has a tree the moment it is picked.
            DevToolsTreeWatcher.Key(session + "-watch").Feed(feed),
            DevToolsFlashEmitter.Key(session + "-flash").Feed(feed).On(_flash).OnChange(on => _flash = on),
            DevToolsPatchReceiver.Key(session + "-patch").Feed(feed),
            DevToolsPageErrorReceiver.Key(session + "-page-errors").Feed(feed),
            DevToolsTabs.Key(session + "-tabs")
                .Current(_tab)
                .PageErrors(feed.Errors)
                .AppErrors(inspection.AppWide)
                .OnSelect(id =>
                {
                    _tab = id;
                    _reveal = null;
                }),
            tab
        ];
    }
}
