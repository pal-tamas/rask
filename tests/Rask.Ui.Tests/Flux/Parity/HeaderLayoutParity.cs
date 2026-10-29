using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/layouts/header</c>, demo for demo.</summary>
/// <remarks>
///     What a header holds — the brand, the navbars, the profile — is other pages' to draw, and compared as a box
///     (parity.mjs's FOREIGN). The brand and the navbars are the real ones, so each is as wide as its words are on
///     the platform measuring it (whole pixels on Linux, not on a Mac); the profile is a box of the size Flux gave
///     it. What is compared is the header, the phone's sidebar and the main.
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

    // The toggle, the brand, the navbar, a spacer, the icon navbar and the account. Flux's inset demo writes
    // `mr-4` on the icon navbar where the others write `me-4`: the same margin on a left-to-right page.
    private static Component[] Contents(bool inset) =>
    [
        inset
            ? Ui.SidebarToggle.Icon(Ui.IconName.Bars2).Inset(Ui.Position.Left)
            : Ui.SidebarToggle.Icon(Ui.IconName.Bars2),
        Ui.Brand.Href("#").Logo(LayoutDemo.Logo).Name("Acme Inc.").Class("max-lg:hidden dark:hidden"),
        Ui.Brand.Href("#").Logo(LayoutDemo.DarkLogo).Name("Acme Inc.").Class("max-lg:hidden! hidden dark:flex"),
        Ui.Navbar.Class("-mb-px max-lg:hidden")[
            Ui.NavbarItem.Icon(Ui.IconName.Home).Href("#").Current(true)["Home"],
            Ui.NavbarItem.Icon(Ui.IconName.Inbox).Badge("12").Href("#")["Inbox"],
            Ui.NavbarItem.Icon(Ui.IconName.DocumentText).Href("#")["Documents"],
            Ui.NavbarItem.Icon(Ui.IconName.Calendar).Href("#")["Calendar"],
            Ui.Separator.Vertical(true).Variant(Ui.SeparatorVariant.Subtle).Class("my-2"),
            Ui.Dropdown.Class("max-lg:hidden")[
                Ui.NavbarItem.IconTrailing(Ui.IconName.ChevronDown)["Favorites"],
                Ui.Navmenu[
                    Ui.NavmenuItem.Href("#")["Marketing site"],
                    Ui.NavmenuItem.Href("#")["Android app"],
                    Ui.NavmenuItem.Href("#")["Brand guidelines"]
                ]
            ]
        ],
        Ui.Spacer,
        Ui.Navbar.Class("me-4")[
            Ui.NavbarItem.Icon(Ui.IconName.MagnifyingGlass).Href("#").Label("Search"),
            Ui.NavbarItem.Class("max-lg:hidden").Icon(Ui.IconName.Cog6Tooth).Href("#").Label("Settings"),
            Ui.NavbarItem.Class("max-lg:hidden").Icon(Ui.IconName.InformationCircle).Href("#").Label("Help")
        ],
        Div.Class("flex").Data("ui-seam", "dropdown")[Div.Class("slot-profile")]
    ];

    private static Component Sidebar(string classes) =>
        LayoutDemo.Sidebar(Ui.SidebarCollapsible.Mobile, classes, "docked:-mr-2", search: false, star: false, profile: false);
}
