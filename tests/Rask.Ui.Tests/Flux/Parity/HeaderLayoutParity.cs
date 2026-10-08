using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/layouts/header</c>, demo for demo.</summary>
/// <remarks>
///     What a header holds — the brand, the navbars, the profile — is other pages' to draw, so each is a
///     box of the size Flux gave it. What is compared is the header, the phone's sidebar and the main.
/// </remarks>
public sealed partial class HeaderLayoutParity : FluxLayoutParity
{
    private const string Page1 = "min-h-screen bg-white dark:bg-zinc-800 antialiased";
    private const string Bar = LayoutDemo.Ground + " border-b " + LayoutDemo.Edge;
    private const string Side = "lg:hidden " + LayoutDemo.Ground + " border-r " + LayoutDemo.Edge;

    public override string Page => "header";

    public override string AppUtilities => LayoutDemo.Utilities;

    public override IEnumerable<(string Demo, string BodyClass, Component Body)> Demos()
    {
        yield return ("header", Page1, Document([
            Ui.Header.Container(true).Class(Bar)[Contents(inset: true)],
            Sidebar(Side),
            Ui.Main.Container(true)[LayoutDemo.Greeting()]
        ]));

        yield return ("inset-main", "min-h-dvh " + LayoutDemo.Ground, Document([
            Ui.Header.Class(LayoutDemo.Ground)[Contents(inset: true)],
            Sidebar("lg:hidden " + LayoutDemo.Ground),
            Ui.Main.Inset(true).Container(true).Class("lg:mt-0")[LayoutDemo.Greeting()]
        ]));

        yield return ("secondary-sidebar", Page1, Document([
            Ui.Header.Container(true).Class(Bar)[Contents(inset: false)],
            Sidebar(Side),
            Ui.Main.Container(true)[
                Div.Class("demo-row")[
                    Div.Class("demo-side")[Div.Class("slot-navlist")],
                    Div.Class("slot-separator md:hidden"),
                    Div.Class("demo-body")[LayoutDemo.Greeting()]
                ]
            ]
        ]));
    }

    // The toggle, the brand, the navbar, a spacer, the icon navbar and the account.
    private static Component[] Contents(bool inset) =>
    [
        inset
            ? Ui.SidebarToggle.Icon(Ui.IconName.Bars2).Inset(Ui.Position.Left)
            : Ui.SidebarToggle.Icon(Ui.IconName.Bars2),
        Div.Class("slot-brand max-lg:hidden"),
        Div.Class("slot-navbar max-lg:hidden"),
        Ui.Spacer,
        Div.Class("slot-actions"),
        Div.Class("flex").Data("ui-seam", "dropdown")[Div.Class("slot-profile")]
    ];

    private static Component Sidebar(string classes) =>
        LayoutDemo.Sidebar(Ui.SidebarCollapsible.Mobile, classes, "docked:-mr-2", search: false, star: false, profile: false);
}
