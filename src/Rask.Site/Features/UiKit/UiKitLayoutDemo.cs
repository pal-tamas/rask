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
                UiDrawer
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
                UiDivider.Key("d").Text("or"),
                UiJoin.Key("j")[
                    UiButton.Key("1")["«"],
                    UiButton.Key("2")["1"],
                    UiButton.Key("3")["»"]
                ],
                Div.Class("flex flex-wrap items-center gap-6")[
                    UiIndicator.Key("i").Badge(UiBadge.Tone(UiTone.Error)["9"])[
                        UiButton["Inbox"]
                    ],
                    UiAvatar.Key("a").Src("/img/favicon.svg").Alt("The Rask mark").Round(true)
                        .Class("w-12")
                ]
            ]),

        Section(
            "Application layout — sidebar, navigation, separator, spacer, type",
            "Flux UI's layout pieces. The sidebar beside these docs IS UiSidebar: docked from md up, a drawer "
            + "behind the hamburger below it, with no runtime needed to open it. The current item says so with "
            + "aria-current, worked out from the route. UiSidebarHeader and UiSidebarFooter hold their place "
            + "while the navigation between them scrolls, and UiProfile is the account row — with the same "
            + "keyboard menu a dropdown has, and a monogram when there is no picture. A separator carries no "
            + "margin — the page spaces it — and a spacer pushes what follows it to the far end of its row.",
            Div.Data(Testid("ui-app-layout")).Class("grid gap-6 md:grid-cols-[16rem_1fr]")[
                Div.Class("flex h-80 flex-col rounded-xl border border-base-300 p-3")[
                    UiSidebarHeader.Key("head").Class("mb-2 border-b border-base-300 pb-2")[
                        UiBrand.Key("brand").Label("Rask").Href(PageMeta.LinkTo(Routes.UiKitLayoutPage()))
                    ],
                    UiNavList.Key("nav").AccessibleLabel("Demo")[
                        UiNavItem.Key("layout").Label("Layout").Href(PageMeta.LinkTo(Routes.UiKitLayoutPage()))
                            .Icon(UiIconName.Book),
                        UiNavItem.Key("actions").Label("Actions").Href(PageMeta.LinkTo(Routes.UiKitActionsPage()))
                            .Icon(UiIconName.Sparkles).Badge("5").BadgeTone(UiTone.Primary),
                        UiNavGroup.Key("more").Heading("More").Expandable(true)[
                            UiNavItem.Key("feedback").Label("Feedback")
                                .Href(PageMeta.LinkTo(Routes.UiKitFeedbackPage())),
                            UiNavItem.Key("navigation").Label("Navigation")
                                .Href(PageMeta.LinkTo(Routes.UiKitNavigationPage()))
                        ]
                    ],
                    // No UiSpacer in front of it: the footer pins itself, so a nav list long enough to scroll
                    // scrolls between the header and this rather than pushing the account row off the bottom.
                    UiSidebarFooter.Key("foot")[
                        UiProfile.Key("me").Name("Ada Lovelace").Caption("ada@example.com")[
                            UiMenuItem.Key("settings").Text("Settings").Icon(UiIconName.Gear),
                            UiMenuSeparator.Key("sep"),
                            UiMenuItem.Key("out").Text("Sign out").Tone(UiTone.Error)
                        ]
                    ]
                ],
                Div.Class("flex flex-col gap-4")[
                    Div.Data(Testid("ui-spacer-row")).Class("flex items-center gap-2 rounded-xl border border-base-300 p-2")[
                        UiButton.Key("left").Variant(UiVariant.Ghost).Size(UiSize.Sm)["Rask"],
                        UiDivider.Key("bar-sep").Vertical(true).Subtle(true).Class("my-1"),
                        UiButton.Key("docs").Variant(UiVariant.Ghost).Size(UiSize.Sm)["Docs"],
                        UiSpacer.Key("spacer"),
                        UiButton.Key("right").Size(UiSize.Sm)["Sign in"]
                    ],
                    Div[
                        UiHeading.Key("h").Level(3).Size(UiSize.Lg)["Orders"],
                        UiSubheading.Key("sh")["Everything placed in the last 30 days."]
                    ],
                    UiDivider.Key("then").Text("then").Align(UiAlign.Start),
                    UiText.Key("t")["Body copy in the kit's scale. ", UiText.Key("strong").Inline(true).Strong(true)["Strong"],
                        " for what matters, ", UiText.Key("subtle").Inline(true).Subtle(true)["subtle"], " for what can be skipped."]
                ]
            ]),

        Section(
            "Mask",
            "Clipping, so whatever is masked has to survive losing its corners.",
            Div.Data(Testid("ui-layout-mask")).Class("flex flex-wrap gap-3")[
                UiMask.Key("m1").Shape(UiMaskShape.Squircle).Class("size-12 bg-secondary"),
                UiMask.Key("m2").Shape(UiMaskShape.Hexagon).Class("size-12 bg-accent"),
                UiMask.Key("m3").Shape(UiMaskShape.Triangle).Class("size-12 bg-primary")
            ]),

        Section(
            "Mockups",
            "Frames for a screenshot or a snippet. The code block is the only one carrying text, and "
            + "it encodes it — a snippet containing markup has to read as that markup, not become it.",
            Div.Data(Testid("ui-mockups")).Class("space-y-4")[
                UiMockupCode.Key("code").Lines([
                    ("$", "dotnet new install Rask.Templates"),
                    ("$", "rask new shop"),
                    (">", "Scaffolded shop in 1.2s")
                ]),
                UiMockupBrowser.Key("browser").Url("https://rask.sh").Class("border border-base-300")[
                    Div.Class("flex h-24 items-center justify-center bg-base-200 text-sm")[
                        "One C# app, published as static files."
                    ]
                ],
                Div.Class("flex flex-wrap gap-4")[
                    UiMockupWindow.Key("window").Class("border border-base-300")[
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
