namespace Rask.Site.Features.UiKit;

/// <summary>
///     daisyUI's Navigation category, drawn with the kit.
/// </summary>
/// <remarks>
///     The tabs are Flux's, in <see cref="UiKitTabsDemo" />. The rest is made of links rather than held in C#,
///     on purpose: navigation is the first thing a reader touches and the last thing that should wait for a
///     bundle.
/// </remarks>
public sealed partial class UiKitNavigationDemo : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
    [
        TabsSection(),
        MenuStepsAndDockSection()
    ];

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
            "Nav list, steps, breadcrumbs, pagination and the dock",
            "The rest of the category, each a real link where it navigates.",
            Div.Data(Testid("ui-nav-rest")).Class("space-y-4")[
                Ui.Navlist[
                    Ui.NavlistItem.Key("m1").Href("#overview").Current()["Overview"],
                    Ui.NavlistItem.Key("m2").Href("#queues").Current(false)["Queues"],
                    Ui.NavlistItem.Key("m3").Href("#logs").Current(false)["Logs"]
                ],
                Ui.Steps[
                    Ui.Step.Key("s1").Success["Ordered"],
                    Ui.Step.Key("s2").Success["Packed"],
                    Ui.Step.Key("s3")["Shipped"]
                ],
                // The last crumb has no href, because the page you are on is not a link to itself; it says
                // aria-current instead.
                Ui.Breadcrumbs[
                    Ui.BreadcrumbsItem.Key("home").Href("#home")["Home"],
                    Ui.BreadcrumbsItem.Key("orders").Href("#orders")["Orders"],
                    Ui.BreadcrumbsItem.Key("order")["ord_18f"]
                ],
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

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
