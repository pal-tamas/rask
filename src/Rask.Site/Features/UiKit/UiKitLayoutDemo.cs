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
            ]),

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
                    Ui.Indicator.Key("i").Badge(Ui.Badge.Tone(Ui.Tone.Error)["9"])[
                        Ui.Button["Inbox"]
                    ],
                    Ui.Avatar.Key("a").Src("/img/favicon.svg").Alt("The Rask mark").Round(true)
                        .Class("w-12"),
                    // No picture: the monogram stands in. Most accounts have none, and a broken image is
                    // worse than two letters — the NAME is still what a screen reader announces.
                    Ui.Avatar.Key("a2").Name("Ada Lovelace").Size(Ui.Size.Lg)
                ]
            ]),

        Section(
            "Application layout — sidebar, navigation, separator, spacer, type",
            "Flux UI's layout pieces. The sidebar beside these docs IS Ui.Sidebar: docked from md up, a drawer "
            + "behind the hamburger below it, with no runtime needed to open it. The current item says so with "
            + "aria-current, worked out from the route. Ui.SidebarHeader and Ui.SidebarFooter hold their place "
            + "while the navigation between them scrolls, and Ui.Profile is the account row — with the same "
            + "keyboard menu a dropdown has, and a monogram when there is no picture. A separator carries no "
            + "margin — the page spaces it — and a spacer pushes what follows it to the far end of its row.",
            Div.Data(Testid("ui-app-layout")).Class("grid gap-6 md:grid-cols-[16rem_1fr]")[
                Div.Class("flex h-80 flex-col rounded-xl border border-base-300 p-3")[
                    Ui.SidebarHeader.Key("head").Class("mb-2 border-b border-base-300 pb-2")[
                        Ui.Brand.Key("brand").Label("Rask").Href(PageMeta.LinkTo(Routes.UiKitLayoutPage()))
                    ],
                    Ui.NavList.Key("nav").AccessibleLabel("Demo")[
                        Ui.NavItem.Key("layout").Label("Layout").Href(PageMeta.LinkTo(Routes.UiKitLayoutPage()))
                            .Icon(Ui.IconName.Book),
                        Ui.NavItem.Key("actions").Label("Actions").Href(PageMeta.LinkTo(Routes.UiKitActionsPage()))
                            .Icon(Ui.IconName.Sparkles).Badge("5").BadgeTone(Ui.Tone.Primary),
                        Ui.NavGroup.Key("more").Heading("More").Expandable(true)[
                            Ui.NavItem.Key("feedback").Label("Feedback")
                                .Href(PageMeta.LinkTo(Routes.UiKitFeedbackPage())),
                            Ui.NavItem.Key("navigation").Label("Navigation")
                                .Href(PageMeta.LinkTo(Routes.UiKitNavigationPage()))
                        ]
                    ],
                    // No Ui.Spacer in front of it: the footer pins itself, so a nav list long enough to scroll
                    // scrolls between the header and this rather than pushing the account row off the bottom.
                    Ui.SidebarFooter.Key("foot")[
                        Ui.Profile.Key("me").Name("Ada Lovelace").Caption("ada@example.com")[
                            Ui.MenuItem.Key("settings").Text("Settings").Icon(Ui.IconName.Gear),
                            Ui.MenuSeparator.Key("sep"),
                            Ui.MenuItem.Key("out").Text("Sign out").Tone(Ui.Tone.Error)
                        ]
                    ]
                ],
                Div.Class("flex flex-col gap-4")[
                    Div.Data(Testid("ui-spacer-row")).Class("flex items-center gap-2 rounded-xl border border-base-300 p-2")[
                        Ui.Button.Key("left").Variant(Ui.Variant.Ghost).Size(Ui.Size.Sm)["Rask"],
                        Ui.Divider.Key("bar-sep").Vertical(true).Subtle(true).Class("my-1"),
                        Ui.Button.Key("docs").Variant(Ui.Variant.Ghost).Size(Ui.Size.Sm)["Docs"],
                        Ui.Spacer.Key("spacer"),
                        Ui.Button.Key("right").Size(Ui.Size.Sm)["Sign in"]
                    ],
                    Div[
                        Ui.Heading.Key("h").Level(3).Size(Ui.Size.Lg)["Orders"],
                        Ui.Subheading.Key("sh")["Everything placed in the last 30 days."]
                    ],
                    Ui.Divider.Key("then").Text("then").Align(Ui.Align.Start),
                    Ui.Text.Key("t")["Body copy in the kit's scale. ", Ui.Text.Key("strong").Inline(true).Strong(true)["Strong"],
                        " for what matters, ", Ui.Text.Key("subtle").Inline(true).Subtle(true)["subtle"], " for what can be skipped."]
                ]
            ]),

        Section(
            "Mask",
            "Clipping, so whatever is masked has to survive losing its corners.",
            Div.Data(Testid("ui-layout-mask")).Class("flex flex-wrap gap-3")[
                Ui.Mask.Key("m1").Shape(Ui.MaskShape.Squircle).Class("size-12 bg-secondary"),
                Ui.Mask.Key("m2").Shape(Ui.MaskShape.Hexagon).Class("size-12 bg-accent"),
                Ui.Mask.Key("m3").Shape(Ui.MaskShape.Triangle).Class("size-12 bg-primary")
            ]),

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
            ])
    ];

    private static Dictionary<string, string?> Testid(string value) => new() { ["testid"] = value };

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
