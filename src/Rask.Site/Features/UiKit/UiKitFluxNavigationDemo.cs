namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux UI's navigation pieces — navbar and navlist, brand, profile and breadcrumbs — example for
///     example, in the order fluxui.dev shows them.
/// </summary>
/// <remarks>
///     The links here go to <c>#</c>, as Flux's do, so the first of each run states that it is current: a
///     generated route works that out for itself. A profile or a navbar item that opens a menu is shown as the
///     trigger alone — the menu is the dropdown's.
/// </remarks>
public sealed partial class UiKitFluxNavigationDemo : Component
{
    private const string Caleb = "/img/avatars/amber.svg";

    private const string Tray = "flex flex-wrap items-center gap-6";

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        NavbarSection(),
        NavlistSection(),
        BrandSection(),
        ProfileSection(),
        BreadcrumbsSection()
    ];

    private static Component NavbarSection() =>
        Section(
            "Navbar",
            "Flux's navbar: links in a row. The current item is inked and underlined in the accent; an icon "
            + "leads, a badge trails, and an item with nowhere to go is the button that opens a menu.",
            Div.Data(Testid("ui-flux-navbar")).Class("flex flex-col gap-2")[
                Ui.Navbar.Key("plain")[
                    Ui.NavbarItem.Key("navbar-plain-1").Href("#").Current(true)["Home"],
                    Ui.NavbarItem.Key("navbar-plain-2").Href("#")["Features"],
                    Ui.NavbarItem.Key("navbar-plain-3").Href("#")["Pricing"],
                    Ui.NavbarItem.Key("navbar-plain-4").Href("#")["About"]
                ],
                Ui.Navbar.Key("icons")[
                    Ui.NavbarItem.Key("navbar-icons-1").Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
                    Ui.NavbarItem.Key("navbar-icons-2").Href("#").Icon(Ui.IconName.PuzzlePiece)["Features"],
                    Ui.NavbarItem.Key("navbar-icons-3").Href("#").Icon(Ui.IconName.CurrencyDollar)["Pricing"],
                    Ui.NavbarItem.Key("navbar-icons-4").Href("#").Icon(Ui.IconName.User)["About"]
                ],
                Ui.Navbar.Key("badges")[
                    Ui.NavbarItem.Key("navbar-badges-1").Href("#").Current(true)["Home"],
                    Ui.NavbarItem.Key("navbar-badges-2").Href("#").Badge("12")["Inbox"],
                    Ui.NavbarItem.Key("navbar-badges-3").Href("#")["Contacts"],
                    Ui.NavbarItem.Key("navbar-badges-4").Href("#").Badge("Pro").BadgeColor(Ui.Color.Lime)["Calendar"]
                ],
                Ui.Navbar.Key("dropdown")[
                    Ui.NavbarItem.Key("navbar-dropdown-1").Href("#").Current(true).Accent(false)["Dashboard"],
                    Ui.NavbarItem.Key("navbar-dropdown-2").Href("#")["Transactions"],
                    Ui.NavbarItem.Key("navbar-dropdown-3").IconTrailing(Ui.IconName.ChevronDown)["Account"]
                ]
            ]);

    private static Component NavlistSection() =>
        Section(
            "Navlist",
            "The same items in a column — a sidebar's navigation. A group is a heading over its items, or a "
            + "disclosure that folds them away: a <details>, so it opens with a click, Enter or Space before any "
            + "runtime has booted.",
            Div.Data(Testid("ui-flux-navlist")).Class("grid gap-6 sm:grid-cols-2 lg:grid-cols-4")[
                Ui.Navlist.Key("plain")[
                    Ui.NavlistItem.Key("navlist-plain-1").Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
                    Ui.NavlistItem.Key("navlist-plain-2").Href("#").Icon(Ui.IconName.PuzzlePiece)["Features"],
                    Ui.NavlistItem.Key("navlist-plain-3").Href("#").Icon(Ui.IconName.CurrencyDollar)["Pricing"],
                    Ui.NavlistItem.Key("navlist-plain-4").Href("#").Icon(Ui.IconName.User)["About"]
                ],
                Ui.Navlist.Key("group")[
                    Ui.NavlistGroup.Key("group-g").Heading("Account")[
                        Ui.NavlistItem.Key("navlist-group-g-1").Href("#").Current(true)["Profile"],
                        Ui.NavlistItem.Key("navlist-group-g-2").Href("#")["Settings"],
                        Ui.NavlistItem.Key("navlist-group-g-3").Href("#")["Billing"]
                    ]
                ],
                Ui.Navlist.Key("fold")[
                    Ui.NavlistItem.Key("navlist-fold-1").Href("#").Icon(Ui.IconName.Home).Current(true)["Dashboard"],
                    Ui.NavlistItem.Key("navlist-fold-2").Href("#").Icon(Ui.IconName.ListBullet)["Transactions"],
                    Ui.NavlistGroup.Key("fold-g").Heading("Account").Expandable()[
                        Ui.NavlistItem.Key("navlist-fold-g-1").Href("#")["Profile"],
                        Ui.NavlistItem.Key("navlist-fold-g-2").Href("#")["Settings"],
                        Ui.NavlistItem.Key("navlist-fold-g-3").Href("#")["Billing"]
                    ],
                    Ui.NavlistGroup.Key("fold-closed").Heading("Team").Expandable().Expanded(false)[
                        Ui.NavlistItem.Key("navlist-fold-closed-1").Href("#")["Members"]
                    ]
                ],
                Ui.Navlist.Key("badges")[
                    Ui.NavlistItem.Key("navlist-badges-1").Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
                    Ui.NavlistItem.Key("navlist-badges-2").Href("#").Icon(Ui.IconName.Envelope).Badge("12")["Inbox"],
                    Ui.NavlistItem.Key("navlist-badges-3").Href("#").Icon(Ui.IconName.UserGroup)["Contacts"],
                    Ui.NavlistItem.Key("navlist-badges-4").Href("#").Icon(Ui.IconName.CalendarDays).Badge("Pro").BadgeColor(Ui.Color.Lime)["Calendar"]
                ]
            ]);

    private static Component BrandSection() =>
        Section(
            "Brand",
            "The product's mark and name, linking home. The logo is an image's address or anything you draw; "
            + "leave the name out for the mark alone.",
            Div.Data(Testid("ui-flux-brand")).Class(Tray)[
                Ui.Brand.Key("image").Href("#").Logo("/img/rask-mark.svg").Name("Acme Inc."),
                Ui.Brand.Key("slot").Href("#").Name("Launchpad")
                    .Logo(Ui.Icon.Name(Ui.IconName.RocketLaunch).Micro)
                    .LogoClass("rounded-full bg-cyan-500 text-white"),
                Ui.Brand.Key("only").Href("#").Logo("/img/rask-mark.svg").Alt("Acme Inc."),
                Div.Key("header").Class("flex min-h-14 items-center rounded-lg border border-zinc-100 bg-zinc-50 px-4 dark:border-white/5 dark:bg-zinc-800")[
                    Ui.Brand.Href("#").Name("Acme Inc.")
                        .Logo(I.Class("font-serif font-bold")["A"])
                        .LogoClass("bg-zinc-800 text-white dark:bg-white dark:text-zinc-800"),
                    Ui.Navbar[
                        Ui.NavbarItem.Key("header-1").Href("#").Current(true)["Home"],
                        Ui.NavbarItem.Key("header-2").Href("#").Badge("12")["Inbox"]
                    ],
                    Div.Class("min-w-24 flex-1"),
                    Ui.Profile.Circle().Chevron(false).Avatar(Caleb)
                ]
            ]);

    private static Component ProfileSection() =>
        Section(
            "Profile",
            "The signed-in person as a button: avatar, name and a chevron. It is the trigger of an account menu "
            + "or a profile switcher, and shows initials when there is no picture.",
            Div.Data(Testid("ui-flux-profile")).Class(Tray)[
                Ui.Profile.Key("avatar").Avatar(Caleb),
                Ui.Profile.Key("name").Name("Caleb Porzio").Avatar(Caleb),
                Ui.Profile.Key("bare").Chevron(false).Avatar(Caleb),
                Ui.Profile.Key("circle").Circle().Chevron(false).Avatar(Caleb),
                Ui.Profile.Key("circle-name").Circle().Name("Caleb Porzio").Avatar(Caleb),
                Ui.Profile.Key("initials").Name("Caleb Porzio"),
                Ui.Profile.Key("colour").Name("Caleb Porzio").AvatarColor(Ui.Color.Cyan),
                Ui.Profile.Key("stated").Initials("CP"),
                Ui.Profile.Key("avatar-name").AvatarName("Caleb Porzio"),
                Ui.Profile.Key("icon").IconTrailing(Ui.IconName.ChevronUpDown).Avatar(Caleb).Name("Caleb Porzio")
            ]);

    private static Component BreadcrumbsSection() =>
        Section(
            "Breadcrumbs",
            "The trail to the page being shown. A chevron separates the steps and turns with the reading "
            + "direction; a slash is one prop away, and an icon can stand in for a step's words.",
            Div.Data(Testid("ui-flux-breadcrumbs")).Class("flex flex-col gap-3")[
                Ui.Breadcrumbs.Key("plain")[
                    Ui.BreadcrumbsItem.Key("crumbs-plain-1").Href("#")["Home"],
                    Ui.BreadcrumbsItem.Key("crumbs-plain-2").Href("#")["Blog"],
                    Ui.BreadcrumbsItem.Key("crumbs-plain-3")["Post"]
                ],
                Ui.Breadcrumbs.Key("slashes")[
                    Ui.BreadcrumbsItem.Key("crumbs-slashes-1").Href("#").Separator(Ui.IconName.Slash)["Home"],
                    Ui.BreadcrumbsItem.Key("crumbs-slashes-2").Href("#").Separator(Ui.IconName.Slash)["Blog"],
                    Ui.BreadcrumbsItem.Key("crumbs-slashes-3").Separator(Ui.IconName.Slash)["Post"]
                ],
                Ui.Breadcrumbs.Key("icon")[
                    Ui.BreadcrumbsItem.Key("crumbs-icon-1").Href("#").Icon(Ui.IconName.Home),
                    Ui.BreadcrumbsItem.Key("crumbs-icon-2").Href("#")["Blog"],
                    Ui.BreadcrumbsItem.Key("crumbs-icon-3")["Post"]
                ],
                Ui.Breadcrumbs.Key("ellipsis")[
                    Ui.BreadcrumbsItem.Key("crumbs-ellipsis-1").Href("#").Icon(Ui.IconName.Home),
                    Ui.BreadcrumbsItem.Key("crumbs-ellipsis-2").Icon(Ui.IconName.EllipsisHorizontal),
                    Ui.BreadcrumbsItem.Key("crumbs-ellipsis-3")["Post"]
                ]
            ]);

    private static AttrBag Testid(string value) => new("testid", value);

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
