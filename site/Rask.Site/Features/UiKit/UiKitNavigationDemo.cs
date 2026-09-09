namespace Rask.Site.Features.UiKit;

/// <summary>
///     daisyUI's Navigation category, drawn with the kit.
/// </summary>
/// <remarks>
///     Two components here are opened by the BROWSER rather than by C#, and both on purpose. The
///     megamenu is built on the native popover API, and the tab row is made of links. Navigation is the
///     first thing a reader touches and the last thing that should wait for a bundle.
/// </remarks>
public sealed partial class UiKitNavigationDemo : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Section(
            "Megamenu",
            "Native popovers: the browser supplies the top layer, Escape and light-dismiss, and the "
            + "whole thing works before any runtime has booted. daisyUI defines no megamenu-open class, "
            + "so there is nothing for a page to drive.",
            Div.Data(Testid("ui-megamenu")).Class("min-h-32")[
                UiMegamenu.Wide(true)[
                    UiMegamenuPanel.Key("products").Trigger("Products").Id("mm-products")[
                        Div.Class("grid gap-2 p-4 sm:grid-cols-2")[
                            PanelLink("a", "Framework", "The renderer and the chain."),
                            PanelLink("b", "CLI", "Scaffolding and deploys."),
                            PanelLink("c", "UI kit", "The components on this page."),
                            PanelLink("d", "Dashboard", "The operator console.")
                        ]
                    ],
                    UiMegamenuPanel.Key("company").Trigger("Company").Id("mm-company")[
                        Div.Class("grid gap-2 p-4")[
                            PanelLink("e", "About", "Why this exists."),
                            PanelLink("f", "Governance", "How decisions are made.")
                        ]
                    ]
                ]
            ]),

        Section(
            "Tabs",
            "Links, not a selected index — the one component in the kit that deliberately did not move "
            + "onto C# state. A tab that is a URL is bookmarkable, survives a refresh and answers the "
            + "back button.",
            Div.Data(Testid("ui-tabs")).Class("space-y-4")[
                UiTabs.Style(UiTabStyle.Box)[
                    UiTab.Key("all").Href("#all").Label("All").Active(true).Count("128"),
                    UiTab.Key("open").Href("#open").Label("Open").Count("12"),
                    UiTab.Key("failed").Href("#failed").Label("Failed").Count("3").Alarm(true)
                ],
                UiTabs.Style(UiTabStyle.Border).Size(UiSize.Sm)[
                    UiTab.Key("b1").Href("#one").Label("Bordered").Active(true),
                    UiTab.Key("b2").Href("#two").Label("Second"),
                    UiTab.Key("b3").Href("#three").Label("Unavailable").Disabled(true)
                ],
                UiTabs.Style(UiTabStyle.Lift)[
                    UiTab.Key("l1").Href("#one").Label("Lifted").Active(true),
                    UiTab.Key("l2").Href("#two").Label("Second")
                ]
            ]),

        Section(
            "Menu, steps, breadcrumbs, pagination and the dock",
            "The rest of the category, each a real link where it navigates.",
            Div.Data(Testid("ui-nav-rest")).Class("space-y-4")[
                UiMenu.Size(UiSize.Sm).Horizontal(true)[
                    UiMenuItem.Key("m1").Text("Overview").Href("#overview").Active(true),
                    UiMenuItem.Key("m2").Text("Queues").Href("#queues"),
                    UiMenuItem.Key("m3").Text("Logs").Href("#logs")
                ],
                UiSteps[
                    UiStep.Key("s1").Text("Ordered").Tone(UiTone.Success),
                    UiStep.Key("s2").Text("Packed").Tone(UiTone.Success),
                    UiStep.Key("s3").Text("Shipped")
                ],
                // Already data-shaped: the crumbs are a list of (text, href), and the last one has no
                // href because the page you are on is not a link to itself.
                UiBreadcrumbs.Items([("Home", "#home"), ("Orders", "#orders"), ("ord_18f", null)])
            ])
    ];

    private static Dictionary<string, string?> Testid(string value) => new() { ["testid"] = value };

    private static Component PanelLink(string key, string title, string blurb) =>
        A.Key(key).Href("#").Class("block rounded-lg p-2 no-underline hover:bg-base-200")[
            Div.Class("text-sm font-medium")[title],
            Div.Class("text-xs opacity-60")[blurb]
        ];

    private static new Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
