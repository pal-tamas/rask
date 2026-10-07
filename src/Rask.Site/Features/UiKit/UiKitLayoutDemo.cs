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
        DividerJoinIndicatorSection(),
        ApplicationLayoutSection(),
        MaskSection(),
        MockupsSection()
    ];

    private Component DrawerSection() =>
        Section(
            "Drawer",
            "The one interactive component whose state stays in a checkbox — daisyUI's rules are "
            + "written against it, so removing the input removes the component. C# sets it and hears "
            + "it change, which is what the checkbox alone could not offer.",
            Div.Data(Testid("ui-drawer")).Class("h-56 overflow-hidden rounded-xl border border-base-300")[
                // Id and Panel are both REQUIRED, so the chain owes them before any optional step:
                // .Id(x).Open(y) does not compile until Panel has been supplied.
                Ui.Drawer
                    .Id("demo-drawer")
                    .Panel(
                        Ul.Class("menu min-h-full w-56 bg-base-200 p-4")[
                            Li.Key("a")[A.Href("#")["Overview"]],
                            Li.Key("b")[A.Href("#")["Queues"]],
                            Li.Key("c")[A.Href("#")["Logs"]]
                        ])
                    .Open(_drawerOpen)
                    .OnToggle(open => { _drawerOpen = open; })
                    .CloseLabel("Close navigation")[
                    Div.Class("flex h-full flex-col items-start gap-2 p-4")[
                        Label
                            .For("demo-drawer")
                            .Class("btn btn-sm drawer-button")["Open the drawer"],
                        P.Class("text-sm text-ui-muted").Data(Testid("ui-drawer-state"))[
                            _drawerOpen ? "The page knows it is open." : "The page knows it is closed."
                        ]
                    ]
                ]
            ]);

    private static Component DividerJoinIndicatorSection() =>
        Section(
            "Divider, join, indicator and stack",
            "Structure with no state.",
            Div.Data(Testid("ui-layout-rest")).Class("space-y-4")[
                Ui.Divider.Key("d").Text("or"),
                Ui.Join.Key("j")[
                    Ui.Button.Key("1")["«"],
                    Ui.Button.Key("2")["1"],
                    Ui.Button.Key("3")["»"]
                ],
                Div.Class("flex flex-wrap items-center gap-6")[
                    Ui.Indicator.Key("i").Badge(Ui.Badge.Error["9"])[
                        Ui.Button["Inbox"]
                    ],
                    Ui.Avatar.Key("a").Src("/img/favicon.svg").Alt("The Rask mark").Round()
                        .Class("w-12"),
                    // No picture: the monogram stands in. Most accounts have none, and a broken image is
                    // worse than two letters — the NAME is still what a screen reader announces.
                    Ui.Avatar.Key("a2").Name("Ada Lovelace").Lg
                ]
            ]);

    private static Component ApplicationLayoutSection() =>
        Section(
            "Application layout — header, sidebar, main",
            "Flux UI's two layouts. Ui.Header, Ui.Sidebar and Ui.Main are siblings, and whatever holds the main — "
            + "the body, or this box — becomes the grid: the header across the top, the sidebar down the side. "
            + "A layout is a whole page, so each has a demo of its own: there the sidebar slides over the page on "
            + "a phone and narrows to a rail of icons on a desktop, with no script. The current item says so with "
            + "aria-current, worked out from the route.",
            Div.Data(Testid("ui-app-layout")).Class("grid gap-6")[
                Div.Class("flex flex-wrap gap-3")[
                    Ui.Button.Key("sidebar-demo").Href(PageMeta.LinkTo(Routes.SidebarLayoutDemoPage()))["Open the sidebar layout"],
                    Ui.Button.Key("header-demo").Href(PageMeta.LinkTo(Routes.HeaderLayoutDemoPage()))["Open the header layout"]
                ],
                // Not collapsible here: a sidebar that slides over the page is pinned to the viewport, not to a box.
                Div.Data(Testid("ui-layout-box")).Class("h-96 overflow-hidden rounded-xl border border-zinc-200 bg-white dark:border-zinc-700 dark:bg-zinc-800")[
                    Ui.Sidebar.Key("side").Class("border-r border-zinc-200 bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900")[
                        Ui.SidebarHeader.Key("head")[
                            Ui.SidebarBrand.Key("brand").Name("Rask").Logo("/img/favicon.svg")
                                .Href(PageMeta.LinkTo(Routes.UiKitLayoutPage()))
                        ],
                        Ui.SidebarNav.Key("nav")[
                            Ui.SidebarItem.Key("layout").Href(PageMeta.LinkTo(Routes.UiKitLayoutPage()))
                                .Icon(Ui.IconName.BookOpen)["Layout"],
                            Ui.SidebarItem.Key("actions").Href(PageMeta.LinkTo(Routes.UiKitActionsPage()))
                                .Icon(Ui.IconName.Sparkles).Badge("5")["Actions"],
                            Ui.SidebarGroup.Key("more").Heading("More").Expandable()[
                                Ui.SidebarItem.Key("feedback").Href(PageMeta.LinkTo(Routes.UiKitFeedbackPage()))["Feedback"],
                                Ui.SidebarItem.Key("navigation").Href(PageMeta.LinkTo(Routes.UiKitNavigationPage()))["Navigation"]
                            ]
                        ],
                        Ui.SidebarSpacer.Key("gap"),
                        Ui.SidebarProfile.Key("me").Name("Ada Lovelace")
                    ],
                    Ui.Header.Key("bar").Class("border-b border-zinc-200 dark:border-zinc-700")[
                        Span.Class("text-sm font-medium")["Orders"],
                        Ui.Spacer.Key("bar-gap"),
                        Ui.Button.Key("new").Sm["New order"]
                    ],
                    Ui.Main.Key("main")[
                        Ui.Heading.Key("mh").Level(3).Lg["Orders"],
                        Ui.Subheading.Key("msh")["Everything placed in the last 30 days."]
                    ]
                ],
                Div.Class("flex flex-col gap-4")[
                    Div.Data(Testid("ui-spacer-row")).Class("flex items-center gap-2 rounded-xl border border-base-300 p-2")[
                        Ui.Button.Key("left").Ghost.Sm["Rask"],
                        Ui.Divider.Key("bar-sep").Vertical().Subtle().Class("my-1"),
                        Ui.Button.Key("docs").Ghost.Sm["Docs"],
                        Ui.Spacer.Key("spacer"),
                        Ui.Button.Key("right").Sm["Sign in"]
                    ],
                    Div[
                        Ui.Heading.Key("h").Level(3).Lg["Orders"],
                        Ui.Subheading.Key("sh")["Everything placed in the last 30 days."]
                    ],
                    Ui.Divider.Key("then").Text("then").Align(Ui.Align.Start),
                    Ui.Text.Key("t")["Body copy in the kit's scale. ", Ui.Text.Key("strong").Inline().Strong()["Strong"],
                        " for what matters, ", Ui.Text.Key("subtle").Inline().Subtle()["subtle"], " for what can be skipped."]
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
