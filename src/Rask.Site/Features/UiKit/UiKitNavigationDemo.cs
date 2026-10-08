namespace Rask.Site.Features.UiKit;

/// <summary>
///     daisyUI's Navigation category, drawn with the kit.
/// </summary>
/// <remarks>
///     The tab row is made of links rather than held in C#, on purpose. Navigation is the first thing a
///     reader touches and the last thing that should wait for a bundle.
/// </remarks>
public sealed partial class UiKitNavigationDemo : Component
{
    private string _pane = "details";

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        TabsSection(),
        TabGroupSection(),
        MenuStepsAndDockSection(),
        PaginationSection()
    ];

    private static Component TabsSection() =>
        Section(
            "Tabs",
            "Links, not a selected index — the one component in the kit that deliberately did not move "
            + "onto C# state. A tab that is a URL is bookmarkable, survives a refresh and answers the "
            + "back button.",
            Div.Data(Testid("ui-tabs")).Class("space-y-4")[
                Ui.Tabs.Style(Ui.TabStyle.Box)[
                    Ui.Tab.Key("all").Label("All").Href("#all").Active().Count("128"),
                    Ui.Tab.Key("open").Label("Open").Href("#open").Count("12"),
                    Ui.Tab.Key("failed").Label("Failed").Href("#failed").Count("3").Alarm()
                ],
                Ui.Tabs.Style(Ui.TabStyle.Border).Sm[
                    Ui.Tab.Key("b1").Label("Bordered").Href("#one").Active(),
                    Ui.Tab.Key("b2").Label("Second").Href("#two"),
                    Ui.Tab.Key("b3").Label("Unavailable").Href("#three").Disabled()
                ],
                Ui.Tabs.Style(Ui.TabStyle.Lift)[
                    Ui.Tab.Key("l1").Label("Lifted").Href("#one").Active(),
                    Ui.Tab.Key("l2").Label("Second").Href("#two")
                ]
            ]);

    private Component TabGroupSection() =>
        Section(
            "Tab group — panels for views with no URL",
            "The same Ui.Tab, given a Name instead of an Href, and a Ui.TabPanel per name. For a detail pane "
            + "beside a record, where an address would be inventing state the page does not have. The arrows "
            + "move and show as they go, Home and End jump, and only the selected tab is a tab stop — so Tab "
            + "lands in the panel rather than walking every remaining tab. Every panel is in the markup, so "
            + "the browser's own find-in-page reaches the ones that are not shown.",
            Div.Data(Testid("ui-tab-group"))[
                Ui.TabGroup.Selected(_pane).OnSelect(p => { _pane = p; })[
                    Ui.Tabs.Key("row").Style(Ui.TabStyle.Border)[
                        Ui.Tab.Key("t1").Label("Details").Name("details").Icon(Ui.IconName.BookOpen),
                        Ui.Tab.Key("t2").Label("History").Name("history").Icon(Ui.IconName.Clock).Count("4"),
                        Ui.Tab.Key("t3").Label("Danger").Name("danger").Disabled()
                    ],
                    Ui.TabPanel.Key("p1").Name("details").Class("text-sm")[
                        "Everything about this record that does not change."
                    ],
                    Ui.TabPanel.Key("p2").Name("history").Class("text-sm")[
                        "Four changes, most recent first."
                    ],
                    Ui.TabPanel.Key("p3").Name("danger").Class("text-sm")["Nothing here."]
                ],
                P.Class("mt-2 text-sm text-ui-muted").Data(Testid("ui-tab-group-state"))[$"Showing: {_pane}."]
            ]);

    private static Component MenuStepsAndDockSection() =>
        Section(
            "Nav list, steps, breadcrumbs and the dock",
            "The rest of the category, each a real link where it navigates.",
            Div.Data(Testid("ui-nav-rest")).Class("space-y-4")[
                Ui.NavList.Sm.AccessibleLabel("Sections")[
                    Ui.NavItem.Key("m1").Label("Overview").Href("#overview").Current(),
                    Ui.NavItem.Key("m2").Label("Queues").Href("#queues").Current(false),
                    Ui.NavItem.Key("m3").Label("Logs").Href("#logs").Current(false)
                ],
                Ui.Steps[
                    Ui.Step.Key("s1").Success["Ordered"],
                    Ui.Step.Key("s2").Success["Packed"],
                    Ui.Step.Key("s3")["Shipped"]
                ],
                // Already data-shaped: the crumbs are a list of (text, href), and the last one has no
                // href because the page you are on is not a link to itself.
                Ui.Breadcrumbs.Items([("Home", "#home"), ("Orders", "#orders"), ("ord_18f", null)])
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
