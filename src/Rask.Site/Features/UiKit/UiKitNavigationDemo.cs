namespace Rask.Site.Features.UiKit;

/// <summary>
///     daisyUI's Navigation category, drawn with the kit.
/// </summary>
/// <remarks>
///     The megamenu is opened by the BROWSER rather than by C#, on purpose: it is built on the native
///     popover API. Navigation is the first thing a reader touches and the last thing that should wait for
///     a bundle. The tabs are Flux's, in <see cref="UiKitTabsDemo" />.
/// </remarks>
public sealed partial class UiKitNavigationDemo : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
    [
        MegamenuSection(),
        TabsSection(),
        MenuStepsAndDockSection()
    ];

    private static Component MegamenuSection() =>
        Section(
            "Megamenu",
            "Native popovers: the browser supplies the top layer, Escape and light-dismiss, and the "
            + "whole thing works before any runtime has booted. daisyUI defines no megamenu-open class, "
            + "so there is nothing for a page to drive.",
            Div.Data(Testid("ui-megamenu")).Class("min-h-32")[
                Ui.Megamenu.Wide()[
                    Ui.MegamenuPanel.Key("products").Trigger("Products").Id("mm-products")[
                        Div.Class("grid gap-2 p-4 sm:grid-cols-2")[
                            PanelLink("a", "Framework", "The renderer and the chain."),
                            PanelLink("b", "CLI", "Scaffolding and deploys."),
                            PanelLink("c", "UI kit", "The components on this page."),
                            PanelLink("d", "Dashboard", "The operator console.")
                        ]
                    ],
                    Ui.MegamenuPanel.Key("company").Trigger("Company").Id("mm-company")[
                        Div.Class("grid gap-2 p-4")[
                            PanelLink("e", "About", "Why this exists."),
                            PanelLink("f", "Governance", "How decisions are made.")
                        ]
                    ]
                ]
            ]);

    private static Component TabsSection() =>
        Section(
            "Tabs",
            "Flux's tabs. Which tab is selected is a value like any other control's — bound to a property, "
            + "handed to a handler, or left to the row to keep. The arrow keys move between the tabs, past "
            + "a disabled one and around the ends, and select as they go; only the selected tab is a tab "
            + "stop, so Tab lands in its panel.",
            UiKitTabsDemo);

    private static Component MenuStepsAndDockSection() =>
        Section(
            "Menu, steps, breadcrumbs, pagination and the dock",
            "The rest of the category, each a real link where it navigates.",
            Div.Data(Testid("ui-nav-rest")).Class("space-y-4")[
                Ui.Menu.Sm.Horizontal()[
                    Ui.MenuItem.Key("m1").Text("Overview").Href("#overview").Active(),
                    Ui.MenuItem.Key("m2").Text("Queues").Href("#queues"),
                    Ui.MenuItem.Key("m3").Text("Logs").Href("#logs")
                ],
                Ui.Steps[
                    Ui.Step.Key("s1").Success["Ordered"],
                    Ui.Step.Key("s2").Success["Packed"],
                    Ui.Step.Key("s3")["Shipped"]
                ],
                // Already data-shaped: the crumbs are a list of (text, href), and the last one has no
                // href because the page you are on is not a link to itself.
                Ui.Breadcrumbs.Items([("Home", "#home"), ("Orders", "#orders"), ("ord_18f", null)]),
                // Pages as links: each is the address that page lives at, so it can be shared and answers
                // the back button. The page you are on is not a link either — it says aria-current instead.
                Div.Data(Testid("ui-pagination-links"))[
                    Ui.Pagination
                        .Pages(4)
                        .Current(1)
                        .Href(page => PageMeta.LinkTo(Routes.UiKitNavigationPage() with { QueryString = $"?page={page}" }))
                ]
            ]);

    private static AttrBag Testid(string value) => new("testid", value);

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
