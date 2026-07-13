namespace Rask.Example.Shared.Features;

// Navigation primitives: a BsNavbar shell plus a BsNav list of BsNavItems. Each BsNavItem with an
// Href renders a core NavLink, so the link is SPA-routed and lights up its .active class by matching
// the current route — no client JS, no manual active tracking. This is the same toolkit the showcase's
// own top bar and sidebar are built from.
public sealed class BsNavDemo : Component
{
    protected override Component? Render() =>
    [
        Div(Class: "vstack gap-4")[
            // A navbar shell: brand on the left, an action pushed to the right with the ms-auto utility.
            BsNavbar(Color: BsColor.Dark, Theme: BsTheme.Dark, Class: Bs.Join(Rounded.Default))[
                Span(Class: "navbar-brand mb-0")[BsIcon(Name: BsIconName.Lightning, Class: "me-1"), "Rask"],
                Div(Class: Bs.Join(Display.Flex(), Margin.StartAuto))[
                    BsButton(Color: BsColor.Light, Size: BsSize.Sm)["Sign in"]
                ]
            ],
            // A vertical pills nav — the sidebar pattern. Href makes each item a SPA-routed NavLink that
            // auto-highlights when its route is active (ActiveMatch: Prefix keeps a whole section lit).
            BsNav(Vertical: true, Pills: true)[
                BsNavItem(Href: Features.Routes.GuidesIndexPage())[
                    BsIcon(Name: BsIconName.House, Class: "me-2"), "Guides"],
                BsNavItem(Href: Features.Routes.GuidePage("bootstrap"))[
                    BsIcon(Name: BsIconName.Bootstrap, Class: "me-2"), "Bootstrap guide"],
                BsNavItem(Href: Features.Routes.GuidePage("composition"))[
                    BsIcon(Name: BsIconName.Diagram3, Class: "me-2"), "Composition guide"],
                BsNavItem(Disabled: true)[
                    BsIcon(Name: BsIconName.Hourglass, Class: "me-2"), "Coming soon"]
            ]
        ]
    ];
}
