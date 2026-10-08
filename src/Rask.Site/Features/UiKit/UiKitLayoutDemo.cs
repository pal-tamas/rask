namespace Rask.Site.Features.UiKit;

/// <summary>
///     daisyUI's Layout and Mockup categories, drawn with the kit.
/// </summary>
public sealed partial class UiKitLayoutDemo : Component
{
    private bool _drawerOpen;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        DrawerSection(),
        SeparatorSection(),
        JoinIndicatorSection(),
        ApplicationLayoutSection(),
        TypographySection(),
        MaskSection(),
        MockupsSection()
    ];

    private Component DrawerSection() =>
        Section(
            "Flyout navigation",
            "What a drawer was: Ui.Modal as a flyout from the left, with the page owning whether it is open. "
            + "C# opens it, and OnClose tells the page the reader closed it — the corner button, Escape or a "
            + "click outside.",
            Div.Data(Testid("ui-drawer")).Class("flex flex-col items-start gap-2")[
                Ui.Button.Sm.OnClick(() => { _drawerOpen = true; })["Open the drawer"],
                P.Class("text-sm text-ui-muted").Data(Testid("ui-drawer-state"))[
                    _drawerOpen ? "The page knows it is open." : "The page knows it is closed."
                ],
                Ui.Modal
                    .Flyout()
                    .Left
                    .Open(_drawerOpen)
                    .OnClose(() => { _drawerOpen = false; })[
                    Ul.Class("menu w-56 p-0")[
                        Li.Key("a")[A.Href("#")["Overview"]],
                        Li.Key("b")[A.Href("#")["Queues"]],
                        Li.Key("c")[A.Href("#")["Logs"]]
                    ]
                ]
            ]);

    // Flux UI's separator page, example for example: plain, with text, vertical, limited height, subtle.
    private static Component SeparatorSection() =>
        Section(
            "Separator",
            "A line between sections of content or groups of items. It carries no margin — the page spaces "
            + "it — and a vertical one is as tall as its row until vertical margin shortens it.",
            Div.Data(Testid("ui-separator")).Class("mx-auto flex max-w-sm flex-col gap-8")[
                Ui.Separator.Key("plain"),
                Ui.Separator.Key("text").Text("or"),
                SeparatorRow("vertical", Ui.Separator.Vertical()),
                SeparatorRow("limited", Ui.Separator.Vertical().Class("my-2")),
                SeparatorRow("subtle", Ui.Separator.Vertical().Subtle)
            ]);

    private static Component SeparatorRow(string key, UiSeparator separator) =>
        Div.Key(key).Class("flex items-center justify-center gap-6")[
            Ui.Button.Ghost.Key("theme").Icon(Ui.IconName.Moon).AriaLabel("Switch to dark theme"),
            separator.Key("separator"),
            Ui.Button.Key("login")["Log in"]
        ];

    private static Component JoinIndicatorSection() =>
        Section(
            "Join, indicator and stack",
            "Structure with no state.",
            Div.Data(Testid("ui-layout-rest")).Class("space-y-4")[
                Ui.Join.Key("j")[
                    Ui.Button.Key("1")["«"],
                    Ui.Button.Key("2")["1"],
                    Ui.Button.Key("3")["»"]
                ],
                Div.Class("flex flex-wrap items-center gap-6")[
                    Ui.Indicator.Key("i").Badge(Ui.Badge.Sm.Solid.Rounded().Color(Ui.Color.Red)["9"])[
                        Ui.Button["Inbox"]
                    ],
                    Ui.Avatar.Key("a").Src("/img/favicon.svg").Alt("The Rask mark").Circle().Lg,
                    // No picture: the monogram stands in. Most accounts have none, and a broken image is
                    // worse than two letters — the NAME is still what a screen reader announces.
                    Ui.Avatar.Key("a2").Name("Ada Lovelace").Lg
                ]
            ]);

    private static Component ApplicationLayoutSection() =>
        Section(
            "Application layout — sidebar, navigation, separator, spacer, type",
            "Flux UI's layout pieces. The sidebar beside these docs IS Ui.Sidebar: docked from md up, a drawer "
            + "behind the hamburger below it, with no runtime needed to open it. The current item says so with "
            + "aria-current, worked out from the route. Ui.SidebarHeader and Ui.SidebarFooter hold their place "
            + "while the navigation between them scrolls, and Ui.Profile is the account button — the trigger of an account "
            + "menu, with initials when there is no picture. A separator carries no "
            + "margin — the page spaces it — and a spacer pushes what follows it to the far end of its row.",
            Div.Data(Testid("ui-app-layout")).Class("grid gap-6 md:grid-cols-[16rem_1fr]")[
                Div.Class("flex h-80 flex-col rounded-xl border border-base-300 p-3")[
                    Ui.SidebarHeader.Key("head").Class("mb-2 border-b border-base-300 pb-2")[
                        Ui.Brand.Key("brand").Name("Rask").Logo(RaskLogo.Size(20).GradientId("demoBolt"))
                            .Href(PageMeta.LinkTo(Routes.UiKitLayoutPage()))
                    ],
                    Ui.Navlist.Key("nav")[
                        Ui.NavlistItem.Key("layout").Href(PageMeta.LinkTo(Routes.UiKitLayoutPage()))
                            .Icon(Ui.IconName.BookOpen)["Layout"],
                        Ui.NavlistItem.Key("actions").Href(PageMeta.LinkTo(Routes.UiKitActionsPage()))
                            .Icon(Ui.IconName.Sparkles).Badge("5").BadgeColor(Ui.Color.Blue)["Actions"],
                        Ui.NavlistGroup.Key("more").Heading("More").Expandable()[
                            Ui.NavlistItem.Key("feedback").Href(PageMeta.LinkTo(Routes.UiKitFeedbackPage()))["Feedback"],
                            Ui.NavlistItem.Key("navigation").Href(PageMeta.LinkTo(Routes.UiKitNavigationPage()))["Navigation"]
                        ]
                    ],
                    // No Ui.Spacer in front of it: the footer pins itself, so a nav list long enough to scroll
                    // scrolls between the header and this rather than pushing the account row off the bottom.
                    Ui.SidebarFooter.Key("foot")[
                        Ui.Dropdown.Key("me").Top.Start.Class("w-full")[
                            Ui.Profile.Name("Ada Lovelace").Class("w-full"),
                            Ui.Menu[
                                Ui.MenuItem.Key("settings").Icon(Ui.IconName.Cog6Tooth)["Settings"],
                                Ui.MenuSeparator.Key("sep"),
                                Ui.MenuItem.Key("out").Danger["Sign out"]
                            ]
                        ]
                    ]
                ],
                Div.Class("flex flex-col gap-4")[
                    Div.Data(Testid("ui-spacer-row")).Class("flex items-center gap-2 rounded-xl border border-base-300 p-2")[
                        Ui.Button.Key("left").Ghost.Sm["Rask"],
                        Ui.Separator.Key("bar-sep").Vertical().Subtle.Class("my-1"),
                        Ui.Button.Key("docs").Ghost.Sm["Docs"],
                        Ui.Spacer.Key("spacer"),
                        Ui.Button.Sm.Key("right")["Sign in"]
                    ],
                    Div[
                        Ui.Heading.Key("h").Level(3).Lg["Orders"],
                        Ui.Text.Key("sh").Class("mt-2")["Everything placed in the last 30 days."]
                    ],
                    Ui.Separator.Key("then").Text("then"),
                    Ui.Text.Key("t")["Body copy in the kit's scale. ", Ui.Text.Key("strong").Inline().Variant(Ui.TextVariant.Strong)["Strong"],
                        " for what matters, ", Ui.Text.Key("subtle").Inline().Subtle["subtle"], " for what can be skipped."]
                ]
            ]);

    // Flux UI's own examples for heading, text and link, in its order.
    private static Component TypographySection() =>
        Section(
            "Heading, text and link",
            "Flux UI's type: a heading whose size and outline level are separate, body copy in three inks or any "
            + "Tailwind hue, and a link that takes the accent.",
            Div.Data(Testid("ui-typography")).Class("grid gap-8 sm:grid-cols-2")[
                Div.Key("sizes").Class("flex flex-col gap-4")[
                    Ui.Heading.Key("base")["Default"],
                    Ui.Heading.Key("lg").Lg["Large"],
                    Ui.Heading.Key("xl").Xl["Extra large"],
                    Ui.Heading.Key("xxl").Xxl["Extra extra large"]
                ],
                Div.Key("level")[
                    Ui.Heading.Key("h").Level(3)["User profile"],
                    Ui.Text.Key("t").Class("mt-2")["This information will be displayed publicly."]
                ],
                Div.Key("leading")[
                    Ui.Text.Key("t")["Year to date"],
                    Ui.Heading.Key("h").Xl.Class("mb-1")["$7,532.16"],
                    Ui.Text.Key("up").Color(Ui.Color.Green)["15.2%"]
                ],
                Div.Key("inks").Class("flex flex-col gap-2")[
                    Ui.Text.Key("strong").Variant(Ui.TextVariant.Strong)["Strong text color"],
                    Ui.Text.Key("default")["Default text color"],
                    Ui.Text.Key("subtle").Subtle["Subtle text color"],
                    Ui.Text.Key("blue").Color(Ui.Color.Blue)["Colored text"]
                ],
                Div.Key("text-sizes").Class("flex flex-col gap-2")[
                    Ui.Text.Key("lg").Lg["Larger text size"],
                    Ui.Text.Key("default")["Default text size"],
                    Ui.Text.Key("sm").Sm["Smaller text"]
                ],
                Div.Key("links").Class("flex flex-col gap-2")[
                    Ui.Text.Key("in-text")["Visit our ",
                        Ui.Link.Key("docs").Href("https://rask.sh/docs").External()["documentation"], " for more information."],
                    Ui.Text.Key("default")[Ui.Link.Key("l").Href("#")["Default link"]],
                    Ui.Text.Key("ghost")[Ui.Link.Key("l").Href("#").Ghost["Ghost link"]],
                    Ui.Text.Key("subtle")[Ui.Link.Key("l").Href("#").Subtle["Subtle link"]],
                    Ui.Text.Key("button")[Ui.Link.Key("l").As(Ui.LinkAs.Button)["Create new account →"]]
                ]
            ]);

    private static Component MaskSection() =>
        Section(
            "Mask",
            "Clipping, so whatever is masked has to survive losing its corners.",
            Div.Data(Testid("ui-layout-mask")).Class("flex flex-wrap gap-3")[
                Ui.Mask.Key("m1").Shape(Ui.MaskShape.Squircle).Class("size-12 bg-secondary"),
                Ui.Mask.Key("m2").Shape(Ui.MaskShape.Hexagon).Class("size-12 bg-accent"),
                Ui.Mask.Key("m3").Shape(Ui.MaskShape.Triangle).Class("size-12 bg-primary")
            ]);

    private static Component MockupsSection() =>
        Section(
            "Mockups",
            "Frames for a screenshot or a snippet. The code block is the only one carrying text, and "
            + "it encodes it — a snippet containing markup has to read as that markup, not become it.",
            Div.Data(Testid("ui-mockups")).Class("space-y-4")[
                Ui.MockupCode.Key("code").Lines([
                    ("$", "dotnet new install Rask.Templates"),
                    ("$", "rask new shop"),
                    (">", "Scaffolded shop in 1.2s")
                ]),
                Ui.MockupBrowser.Key("browser").Url("https://rask.sh").Class("border border-base-300")[
                    Div.Class("flex h-24 items-center justify-center bg-base-200 text-sm")[
                        "One C# app, published as static files."
                    ]
                ],
                Div.Class("flex flex-wrap gap-4")[
                    Ui.MockupWindow.Key("window").Class("border border-base-300")[
                        Div.Class("flex h-20 w-64 items-center justify-center bg-base-200 text-sm")[
                            "A window"
                        ]
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
