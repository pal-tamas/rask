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
                // Id and Side are both REQUIRED, so the chain owes them before any optional step:
                // .Id(x).Open(y) does not compile until Side has been supplied.
                UiDrawer
                    .Id("demo-drawer")
                    .Side(
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
                    UiButton.Key("3")
["»"]                ],
                Div.Class("flex flex-wrap items-center gap-6")[
                    UiIndicator.Key("i").Badge(UiBadge.Label("9").Tone("error"))[
                        UiButton
["Inbox"]                    ],
                    UiAvatar.Key("a").Src("/img/favicon.svg").Alt("The Rask mark").Round(true)
                        .Class("w-12")
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
