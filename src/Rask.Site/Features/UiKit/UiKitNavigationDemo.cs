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
        MenuStepsAndDockSection(),
        PaginationSection()
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
            "Nav list, steps, breadcrumbs and the dock",
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
                ]
            ]);

    private static Component PaginationSection() =>
        Section(
            "Pagination",
            "Flux's pager: a summary, Previous and Next, and the pages numbered where there is room. Buttons "
            + "that report the page chosen, or links where each page has an address.",
            UiKitPaginationDemo);

    private static AttrBag Testid(string value) => new("testid", value);

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
