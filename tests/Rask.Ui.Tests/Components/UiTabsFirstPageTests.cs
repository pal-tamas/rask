using Rask.Core;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>A drivers list paged and sorted in its address, its tabs above it kept there too by the page's own move.</summary>
[Route("/ui-tabs-tests/drivers")]
public sealed partial class TabbedDriversPage(RouteState route) : Component
{
    [QueryParam("active")] public string? Active { get; set; }

    [QueryParam("page")] public int? Number { get; set; }

    [QueryParam("dir")] public string? Dir { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Main[
            Ui.Tabs.Value(Active ?? "active").OnChange(tab => Go.With("active", tab == "active" ? null : tab))[
                Ui.Tab.Name("active")["Active"],
                Ui.Tab.Name("no")["Inactive"]
            ],
            P.Id("at")[string.Join('&', route.Query.OrderBy(q => q.Key, StringComparer.Ordinal).Select(q => q.Key + "=" + q.Value))],
            Ui.Table.Of(FilteredOrders.Rows).SortIn("dir").PageIn("page", 40, perPage: 10)[
                Ui.TableColumns[Ui.TableColumn.Sortable()["Name"]]
            ]
        ];
}

/// <summary>A drivers list whose tabs each move the address themselves as they are pressed.</summary>
[Route("/ui-tabs-tests/pressed")]
public sealed partial class PressedTabsDriversPage(RouteState route) : Component
{
    [QueryParam("status")] public string? Status { get; set; }

    [QueryParam("page")] public int? Number { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Main[
            Ui.Tabs.Segmented[
                Ui.Tab.Key("all").OnClick(() => Go.Without("status"))["All"],
                Ui.Tab.Key("active").OnClick(() => Go.With("status", "active"))["Active"]
            ],
            P.Id("at")[string.Join('&', route.Query.OrderBy(q => q.Key, StringComparer.Ordinal).Select(q => q.Key + "=" + q.Value))],
            Ui.Table.Of(FilteredOrders.Rows).PageIn("page", 40, perPage: 10)[Ui.TableColumns[Ui.TableColumn["Name"]]]
        ];
}

/// <summary>A page's own tabs that switch nothing in the address, above a paged list.</summary>
[Route("/ui-tabs-tests/views")]
public sealed partial class TabbedViewsPage(RouteState route) : Component
{
    private string _view = "list";

    [QueryParam("page")] public int? Number { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Main[
            Ui.TabGroup[
                Ui.Tabs.Value(_view).OnChange(view => { _view = view; })[Ui.Tab.Name("list")["List"], Ui.Tab.Name("board")["Board"]],
                Ui.TabPanel.Name("list")["The list"],
                Ui.TabPanel.Name("board")["The board"]
            ],
            P.Id("at")[string.Join('&', route.Query.Select(q => q.Key + "=" + q.Value))],
            Ui.Table.Of(FilteredOrders.Rows).PageIn("page", 40, perPage: 10)[Ui.TableColumns[Ui.TableColumn["Name"]]]
        ];
}

/// <summary>A tab pressed above a list starts it at its first page, its filters and its sort kept (TA's PagingTests).</summary>
[Collection(FilteredOrdersCollection.Name)]
public partial class UiTabsFirstPageTests : global::Rask.Core.RaskMarkup
{
    private const string Field = "[role=\"tablist\"] > input[type=\"hidden\"]";

    [Fact]
    public async Task Another_tab_starts_the_list_at_its_first_page_and_keeps_its_sort()
    {
        var page = Page.Visit("/ui-tabs-tests/drivers?page=3&dir=desc");

        await page.On(Field).Change("no");

        Assert.Equal("active=no&dir=desc", page.TextOf("#at"));
    }

    [Fact]
    public async Task A_tab_pressed_on_the_first_page_leaves_the_address_as_the_page_moved_it()
    {
        var page = Page.Visit("/ui-tabs-tests/drivers?dir=desc");

        await page.On(Field).Change("no");

        Assert.Equal("active=no&dir=desc", page.TextOf("#at"));
    }

    [Fact]
    public async Task A_tab_whose_own_press_moves_the_address_starts_the_list_at_its_first_page()
    {
        var page = Page.Visit("/ui-tabs-tests/pressed?page=4");

        await page.On("[data-rask-tab=\"1\"]").Click();

        Assert.Equal("status=active", page.TextOf("#at"));
    }

    [Fact]
    public async Task A_tab_group_switching_its_own_panels_leaves_the_lists_page_alone()
    {
        var page = Page.Visit("/ui-tabs-tests/views?page=3");

        await page.On(Field).Change("board");

        Assert.Equal("page=3", page.TextOf("#at"));
    }
}
