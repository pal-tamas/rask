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
    private string _pane = "details";

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
                    UiTab.Key("all").Label("All").Href("#all").Active(true).Count("128"),
                    UiTab.Key("open").Label("Open").Href("#open").Count("12"),
                    UiTab.Key("failed").Label("Failed").Href("#failed").Count("3").Alarm(true)
                ],
                UiTabs.Style(UiTabStyle.Border).Size(UiSize.Sm)[
                    UiTab.Key("b1").Label("Bordered").Href("#one").Active(true),
                    UiTab.Key("b2").Label("Second").Href("#two"),
                    UiTab.Key("b3").Label("Unavailable").Href("#three").Disabled(true)
                ],
                UiTabs.Style(UiTabStyle.Lift)[
                    UiTab.Key("l1").Label("Lifted").Href("#one").Active(true),
                    UiTab.Key("l2").Label("Second").Href("#two")
                ]
            ]),

        Section(
            "Tab group — panels for views with no URL",
            "The same UiTab, given a Name instead of an Href, and a UiTabPanel per name. For a detail pane "
            + "beside a record, where an address would be inventing state the page does not have. The arrows "
            + "move and show as they go, Home and End jump, and only the selected tab is a tab stop — so Tab "
            + "lands in the panel rather than walking every remaining tab. Every panel is in the markup, so "
            + "the browser's own find-in-page reaches the ones that are not shown.",
            Div.Data(Testid("ui-tab-group"))[
                UiTabGroup.Selected(_pane).OnSelect(p => { _pane = p; })[
                    UiTabs.Key("row").Style(UiTabStyle.Border)[
                        UiTab.Key("t1").Label("Details").Name("details").Icon(UiIconName.Book),
                        UiTab.Key("t2").Label("History").Name("history").Icon(UiIconName.Clock).Count("4"),
                        UiTab.Key("t3").Label("Danger").Name("danger").Disabled(true)
                    ],
                    UiTabPanel.Key("p1").Name("details").Class("text-sm")[
                        "Everything about this record that does not change."
                    ],
                    UiTabPanel.Key("p2").Name("history").Class("text-sm")[
                        "Four changes, most recent first."
                    ],
                    UiTabPanel.Key("p3").Name("danger").Class("text-sm")["Nothing here."]
                ],
                P.Class("mt-2 text-sm text-ui-muted").Data(Testid("ui-tab-group-state"))[$"Showing: {_pane}."]
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
                UiBreadcrumbs.Items([("Home", "#home"), ("Orders", "#orders"), ("ord_18f", null)]),
                // Pages as links: each is the address that page lives at, so it can be shared and answers
                // the back button. The page you are on is not a link either — it says aria-current instead.
                Div.Data(Testid("ui-pagination-links"))[
                    UiPagination
                        .Pages(4)
                        .Current(1)
                        .Href(page => PageMeta.LinkTo(Routes.UiKitNavigationPage() with { QueryString = $"?page={page}" }))
                ]
            ])
    ];

    private static Dictionary<string, string?> Testid(string value) => new() { ["testid"] = value };

    private static Component PanelLink(string key, string title, string blurb) =>
        A.Key(key).Href("#").Class("block rounded-lg p-2 no-underline hover:bg-base-200")[
            Div.Class("text-sm font-medium")[title],
            Div.Class("text-xs opacity-60")[blurb]
        ];

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
