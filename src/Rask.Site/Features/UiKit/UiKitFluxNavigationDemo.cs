namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux UI's navigation pieces — navbar and navlist, brand, profile, breadcrumbs and avatar — example for
///     example, in the order fluxui.dev shows them.
/// </summary>
/// <remarks>
///     The links here go to <c>#</c>, as Flux's do, so the first of each run states that it is current: a
///     generated route works that out for itself. A profile or a navbar item that opens a menu is shown as the
///     trigger alone — the menu is the dropdown's.
/// </remarks>
public sealed partial class UiKitFluxNavigationDemo : Component
{
    private const string Caleb = "https://unavatar.io/x/calebporzio";

    private const string Hugo = "https://unavatar.io/github/hugosaintemarie";

    private const string Josh = "https://unavatar.io/github/joshhanley";

    private const string Tray = "flex flex-wrap items-center gap-6";

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        NavbarSection(),
        NavlistSection(),
        BrandSection(),
        ProfileSection(),
        BreadcrumbsSection(),
        AvatarSection(),
        AvatarBadgeAndGroupSection()
    ];

    private static Component NavbarSection() =>
        Section(
            "Navbar",
            "Flux's navbar: links in a row. The current item is inked and underlined in the accent; an icon "
            + "leads, a badge trails, and an item with nowhere to go is the button that opens a menu.",
            Div.Data(Testid("ui-flux-navbar")).Class("flex flex-col gap-2")[
                Ui.Navbar.Key("plain")[
                    Ui.NavbarItem.Key("1").Href("#").Current(true)["Home"],
                    Ui.NavbarItem.Key("2").Href("#")["Features"],
                    Ui.NavbarItem.Key("3").Href("#")["Pricing"],
                    Ui.NavbarItem.Key("4").Href("#")["About"]
                ],
                Ui.Navbar.Key("icons")[
                    Ui.NavbarItem.Key("1").Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
                    Ui.NavbarItem.Key("2").Href("#").Icon(Ui.IconName.PuzzlePiece)["Features"],
                    Ui.NavbarItem.Key("3").Href("#").Icon(Ui.IconName.CurrencyDollar)["Pricing"],
                    Ui.NavbarItem.Key("4").Href("#").Icon(Ui.IconName.User)["About"]
                ],
                Ui.Navbar.Key("badges")[
                    Ui.NavbarItem.Key("1").Href("#").Current(true)["Home"],
                    Ui.NavbarItem.Key("2").Href("#").Badge("12")["Inbox"],
                    Ui.NavbarItem.Key("3").Href("#")["Contacts"],
                    Ui.NavbarItem.Key("4").Href("#").Badge("Pro").BadgeColor(Ui.Color.Lime)["Calendar"]
                ],
                Ui.Navbar.Key("dropdown")[
                    Ui.NavbarItem.Key("1").Href("#").Current(true).Accent(false)["Dashboard"],
                    Ui.NavbarItem.Key("2").Href("#")["Transactions"],
                    Ui.NavbarItem.Key("3").IconTrailing(Ui.IconName.ChevronDown)["Account"]
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
                    Ui.NavlistItem.Key("1").Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
                    Ui.NavlistItem.Key("2").Href("#").Icon(Ui.IconName.PuzzlePiece)["Features"],
                    Ui.NavlistItem.Key("3").Href("#").Icon(Ui.IconName.CurrencyDollar)["Pricing"],
                    Ui.NavlistItem.Key("4").Href("#").Icon(Ui.IconName.User)["About"]
                ],
                Ui.Navlist.Key("group")[
                    Ui.NavlistGroup.Key("g").Heading("Account")[
                        Ui.NavlistItem.Key("1").Href("#").Current(true)["Profile"],
                        Ui.NavlistItem.Key("2").Href("#")["Settings"],
                        Ui.NavlistItem.Key("3").Href("#")["Billing"]
                    ]
                ],
                Ui.Navlist.Key("fold")[
                    Ui.NavlistItem.Key("1").Href("#").Icon(Ui.IconName.Home).Current(true)["Dashboard"],
                    Ui.NavlistItem.Key("2").Href("#").Icon(Ui.IconName.ListBullet)["Transactions"],
                    Ui.NavlistGroup.Key("g").Heading("Account").Expandable()[
                        Ui.NavlistItem.Key("1").Href("#")["Profile"],
                        Ui.NavlistItem.Key("2").Href("#")["Settings"],
                        Ui.NavlistItem.Key("3").Href("#")["Billing"]
                    ],
                    Ui.NavlistGroup.Key("closed").Heading("Team").Expandable().Expanded(false)[
                        Ui.NavlistItem.Key("1").Href("#")["Members"]
                    ]
                ],
                Ui.Navlist.Key("badges").Variant(Ui.NavlistVariant.Outline)[
                    Ui.NavlistItem.Key("1").Href("#").Icon(Ui.IconName.Home).Current(true)["Home"],
                    Ui.NavlistItem.Key("2").Href("#").Icon(Ui.IconName.Envelope).Badge("12")["Inbox"],
                    Ui.NavlistItem.Key("3").Href("#").Icon(Ui.IconName.UserGroup)["Contacts"],
                    Ui.NavlistItem.Key("4").Href("#").Icon(Ui.IconName.CalendarDays).Badge("Pro").BadgeColor(Ui.Color.Lime)["Calendar"]
                ]
            ]);

    private static Component BrandSection() =>
        Section(
            "Brand",
            "The product's mark and name, linking home. The logo is an image's address or anything you draw; "
            + "leave the name out for the mark alone.",
            Div.Data(Testid("ui-flux-brand")).Class(Tray)[
                Ui.Brand.Key("image").Href("#").Logo("/img/favicon.svg").Name("Acme Inc."),
                Ui.Brand.Key("slot").Href("#").Name("Launchpad")
                    .Logo(Ui.Icon.Name(Ui.IconName.RocketLaunch).Micro)
                    .LogoClass("rounded-full bg-cyan-500 text-white"),
                Ui.Brand.Key("only").Href("#").Logo("/img/favicon.svg").Alt("Acme Inc."),
                Div.Key("header").Class("flex min-h-14 items-center rounded-lg border border-zinc-100 bg-zinc-50 px-4 dark:border-white/5 dark:bg-zinc-800")[
                    Ui.Brand.Href("#").Name("Acme Inc.")
                        .Logo(I.Class("font-serif font-bold")["A"])
                        .LogoClass("bg-zinc-800 text-white dark:bg-white dark:text-zinc-800"),
                    Ui.Navbar[
                        Ui.NavbarItem.Key("1").Href("#").Current(true)["Home"],
                        Ui.NavbarItem.Key("2").Href("#").Badge("12")["Inbox"]
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
                    Ui.BreadcrumbsItem.Key("1").Href("#")["Home"],
                    Ui.BreadcrumbsItem.Key("2").Href("#")["Blog"],
                    Ui.BreadcrumbsItem.Key("3")["Post"]
                ],
                Ui.Breadcrumbs.Key("slashes")[
                    Ui.BreadcrumbsItem.Key("1").Href("#").Separator(Ui.IconName.Slash)["Home"],
                    Ui.BreadcrumbsItem.Key("2").Href("#").Separator(Ui.IconName.Slash)["Blog"],
                    Ui.BreadcrumbsItem.Key("3").Separator(Ui.IconName.Slash)["Post"]
                ],
                Ui.Breadcrumbs.Key("icon")[
                    Ui.BreadcrumbsItem.Key("1").Href("#").Icon(Ui.IconName.Home),
                    Ui.BreadcrumbsItem.Key("2").Href("#")["Blog"],
                    Ui.BreadcrumbsItem.Key("3")["Post"]
                ],
                Ui.Breadcrumbs.Key("ellipsis")[
                    Ui.BreadcrumbsItem.Key("1").Href("#").Icon(Ui.IconName.Home),
                    Ui.BreadcrumbsItem.Key("2").Icon(Ui.IconName.EllipsisHorizontal),
                    Ui.BreadcrumbsItem.Key("3")["Post"]
                ]
            ]);

    private static Component AvatarSection() =>
        Section(
            "Avatar",
            "A person as a picture, or as initials or an icon when there is none. Five sizes, seventeen hues, "
            + "and a colour picked from the initials so one person is one colour everywhere.",
            Div.Data(Testid("ui-flux-avatar")).Class("flex flex-col gap-4")[
                Div.Key("faces").Class(Tray)[
                    Ui.Avatar.Key("image").Src(Caleb).Name("Caleb Porzio"),
                    Ui.Avatar.Key("tooltip").Tooltip("Caleb Porzio").Src(Caleb).Name("Caleb Porzio"),
                    Ui.Avatar.Key("two").Name("Caleb Porzio"),
                    Ui.Avatar.Key("word").Name("calebporzio"),
                    Ui.Avatar.Key("single").Name("calebporzio").InitialsSingle(),
                    Ui.Avatar.Key("stated").Initials("CP"),
                    Ui.Avatar.Key("circle").Circle().Src(Caleb).Name("Caleb Porzio"),
                    Ui.Avatar.Key("button").As(Ui.AvatarAs.Button).Src(Caleb).Name("Caleb Porzio"),
                    Ui.Avatar.Key("link").Href("https://x.com/calebporzio").Src(Caleb).Name("Caleb Porzio")
                ],
                Div.Key("sizes").Class("flex flex-wrap items-end gap-6")[
                    Ui.Avatar.Key("xl").Xl.Src(Caleb).Name("Caleb Porzio"),
                    Ui.Avatar.Key("lg").Lg.Src(Caleb).Name("Caleb Porzio"),
                    Ui.Avatar.Key("md").Src(Caleb).Name("Caleb Porzio"),
                    Ui.Avatar.Key("sm").Sm.Src(Caleb).Name("Caleb Porzio"),
                    Ui.Avatar.Key("xs").Xs.Src(Caleb).Name("Caleb Porzio"),
                    Ui.Avatar.Key("user").Icon(Ui.IconName.User),
                    Ui.Avatar.Key("phone").Icon(Ui.IconName.Phone),
                    Ui.Avatar.Key("desktop").Icon(Ui.IconName.ComputerDesktop)
                ],
                Div.Key("colours").Data(Testid("ui-flux-avatar-colors")).Class("flex flex-wrap gap-2")[
                    Enum.GetValues<Ui.Color>().Take(17).Select(Component (color) =>
                        Ui.Avatar.Key(color.ToString()).Name("Caleb Porzio").Color(color))
                ],
                Div.Key("auto").Data(Testid("ui-flux-avatar-auto")).Class("flex flex-wrap gap-2")[
                    Ui.Avatar.Key("cp").Name("Caleb Porzio").ColorAuto(),
                    Ui.Avatar.Key("mj").Name("Mia Jones").ColorAuto(),
                    Ui.Avatar.Key("kc").Name("Kai Chen").ColorAuto(),
                    Ui.Avatar.Key("seed").Name("Caleb Porzio").ColorAuto().ColorSeed("user-42")
                ]
            ]);

    private static Component AvatarBadgeAndGroupSection() =>
        Section(
            "Avatar badges and groups",
            "A badge marks a corner — a plain dot, a count, an emoji or anything else — and a group stacks "
            + "avatars, ringing each in the colour of the page behind it.",
            Div.Data(Testid("ui-flux-avatar-groups")).Class(Tray)[
                Ui.Avatar.Key("dot").Badge("").BadgeColor(Ui.Color.Green).Src(Caleb).Name("Caleb Porzio"),
                Ui.Avatar.Key("outline").Badge("").BadgeColor(Ui.Color.Zinc).BadgePosition(Ui.AvatarBadgePosition.TopRight)
                    .BadgeCircle().BadgeVariant(Ui.AvatarBadgeVariant.Outline).Src(Caleb).Name("Caleb Porzio"),
                Ui.Avatar.Key("count").Badge("25").Src(Caleb).Name("Caleb Porzio"),
                Ui.Avatar.Key("emoji").Circle().Badge("👍").BadgeCircle().Src(Caleb).Name("Caleb Porzio"),
                Ui.Avatar.Key("slot").Circle().Badge(Img.Src(Hugo).Alt("").Class("size-3")).Src(Caleb).Name("Caleb Porzio"),
                Ui.AvatarGroup.Key("group")[
                    Ui.Avatar.Key("1").Src(Caleb).Name("Caleb Porzio"),
                    Ui.Avatar.Key("2").Src(Hugo).Name("Hugo Sainte-Marie"),
                    Ui.Avatar.Key("3").Src(Josh).Name("Josh Hanley"),
                    Ui.Avatar.Key("4")["3+"]
                ],
                Div.Key("ground").Class("rounded-lg bg-zinc-100 p-3 dark:bg-zinc-800")[
                    Ui.AvatarGroup.Class("*:ring-zinc-100 dark:*:ring-zinc-800")[
                        Ui.Avatar.Key("1").Circle().Lg.Src(Caleb).Name("Caleb Porzio"),
                        Ui.Avatar.Key("2").Circle().Lg.Src(Hugo).Name("Hugo Sainte-Marie"),
                        Ui.Avatar.Key("3").Circle().Lg.Src(Josh).Name("Josh Hanley"),
                        Ui.Avatar.Key("4").Circle().Lg["3+"]
                    ]
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
