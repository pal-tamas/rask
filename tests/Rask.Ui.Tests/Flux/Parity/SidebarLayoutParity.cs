using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/layouts/sidebar</c>, demo for demo.</summary>
public sealed partial class SidebarLayoutParity : FluxLayoutParity
{
    private const string Page1 = "min-h-screen bg-white dark:bg-zinc-800 antialiased";
    private const string Bordered = LayoutDemo.Ground + " border-r " + LayoutDemo.Edge;

    public override string Page => "sidebar";

    public override string AppUtilities => LayoutDemo.Utilities;

    public override IEnumerable<(string Demo, string BodyClass, Component Body)> Demos()
    {
        yield return ("sidebar", Page1, Document([
            LayoutDemo.Sidebar(Ui.SidebarCollapsible.Mobile, Bordered, "lg:hidden", search: true, star: false, profile: true),
            Ui.Header.Class("lg:hidden")[LayoutDemo.Bar()],
            Ui.Main[LayoutDemo.Greeting()]
        ]));

        yield return ("inset-main", "min-h-dvh " + LayoutDemo.Ground, Document([
            LayoutDemo.Sidebar(Ui.SidebarCollapsible.Always, LayoutDemo.Ground, "docked:-mr-2", search: false, star: true, profile: true),
            Ui.Header.Class("lg:hidden bg-white dark:bg-zinc-800")[LayoutDemo.Bar()],
            Ui.Main.Inset(true).Class("lg:ms-0")[LayoutDemo.Greeting()]
        ]));

        yield return ("secondary-header", Page1, Document([
            LayoutDemo.Sidebar(Ui.SidebarCollapsible.Mobile, Bordered, "lg:hidden", search: true, star: false, profile: true),
            // Two navbars: the phone's bar, and the tabs. Each is the navbar page's to draw.
            Ui.Header.Class("block! bg-white lg:bg-zinc-50 dark:bg-zinc-900 border-b " + LayoutDemo.Edge)[
                Div.Class("slot-bar lg:hidden"),
                Div.Class("slot-tabs")
            ],
            Ui.Main[LayoutDemo.Greeting()]
        ]));

        yield return ("collapsible-sidebar", Page1, Document([
            LayoutDemo.Sidebar(Ui.SidebarCollapsible.Always, Bordered, "docked:-mr-2", search: false, star: true, profile: true),
            Ui.Header.Class("lg:hidden")[LayoutDemo.Bar()],
            Ui.Main[LayoutDemo.Greeting()]
        ]));

        // Not one of Flux's demos, so `parity.mjs` never asks for it: an application's long menu, for rail.mjs.
        yield return ("long-menu", Page1, Document([
            LayoutDemo.LongMenu(Bordered),
            Ui.Header.Class("lg:hidden")[LayoutDemo.Bar()],
            Ui.Main[LayoutDemo.Greeting()]
        ]));
    }
}
