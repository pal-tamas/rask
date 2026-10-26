using System.Globalization;
using Rask.Core;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>An orders list paged by its address, its first page written with no <c>page</c> at all.</summary>
[Route("/ui-filters-tests/paged")]
public sealed partial class PagedOrdersPage(RouteState route) : Component
{
    [QueryParam("driver")] public string? Driver { get; set; }

    [QueryParam("page")] public int? Number { get; set; }

    [QueryParam("sort")] public string? Sort { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var filters = Ui.Filters[
            Ui.Select.Bind(() => Driver).Label("Driver").Placeholder("Anyone")[
                Ui.SelectOption.Value("ada")["Ada Lovelace"],
                Ui.SelectOption.Value("bob")["Bob Ross"]
            ],
            Ui.Filter.Label("Tag").Shown(FilteredOrders.Tag).OnRemove(() => { FilteredOrders.Tag = null; })
        ];

        return Main[
            filters,
            filters.Badges,
            P.Id("at")[string.Join('&', route.Query.OrderBy(q => q.Key, StringComparer.Ordinal).Select(q => q.Key + "=" + q.Value))],
            Ui.Table.Of(FilteredOrders.Rows).Paginate(
                Ui.Pagination.Paginator(new UiPaginator { Page = Number ?? 1, PerPage = 1, Total = 5 }).Href(page => Link(page)))[
                Ui.TableColumns[Ui.TableColumn["Order"]]
            ]
        ];
    }

    private string Link(int page) => page == 1
        ? $"/ui-filters-tests/paged?driver={Driver}&sort={Sort}"
        : $"/ui-filters-tests/paged?driver={Driver}&page={page.ToString(CultureInfo.InvariantCulture)}&sort={Sort}";
}

/// <summary>An orders list whose page is the page's own state, turned by the pager's buttons.</summary>
[Route("/ui-filters-tests/turned")]
public sealed partial class TurnedOrdersPage(RouteState route) : Component
{
    private int _page = 3;

    [QueryParam("driver")] public string? Driver { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var filters = Ui.Filters[Ui.Select.Bind(() => Driver).Label("Driver")[Ui.SelectOption.Value("ada")["Ada Lovelace"]]];

        return Main[
            filters,
            filters.Badges,
            P.Id("page")[_page.ToString(CultureInfo.InvariantCulture)],
            P.Id("at")[string.Join('&', route.Query.Select(q => q.Key + "=" + q.Value))],
            Ui.Table.Of(FilteredOrders.Rows).Paginate(
                Ui.Pagination.Paginator(new UiPaginator { Page = _page, PerPage = 1, Total = 5 }).OnPage(page => { _page = page; }))[
                Ui.TableColumns[Ui.TableColumn["Order"]]
            ]
        ];
    }
}

/// <summary>A filter change takes the list back to its first page, and the sort stays.</summary>
public partial class UiFiltersFirstPageTests : global::Rask.Core.RaskMarkup
{
    private static Task Open(Page page) => page.On("dialog > input[data-rask-modal-shown]").Change("true");

    [Fact]
    public void The_page_key_is_the_one_the_first_two_pages_disagree_on()
    {
        var first = new RouteUrl("/orders", "?page=1&sort=name");
        var second = new RouteUrl("/orders", "?page=2&sort=name");

        var keys = UiFilterFirstPage.Keys(first, second);

        Assert.Equal([new KeyValuePair<string, string?>("page", "1")], keys);
    }

    [Fact]
    public void A_first_page_written_without_the_key_takes_it_out()
    {
        RouteUrl first = "/orders?sort=name";
        RouteUrl second = "/orders?p=2&sort=name#rows";

        var keys = UiFilterFirstPage.Keys(first, second);

        Assert.Equal([new KeyValuePair<string, string?>("p", null)], keys);
    }

    [Fact]
    public void Keys_the_pages_agree_on_and_a_page_kept_in_the_path_are_left_alone()
    {
        var agreed = UiFilterFirstPage.Keys(new RouteUrl("/orders", "?sort=name&q=tea"), new RouteUrl("/orders", "?q=tea&sort=name"));
        var inThePath = UiFilterFirstPage.Keys(new RouteUrl("/orders/page/1", "?sort=name"), new RouteUrl("/orders/page/2", "?sort=name"));

        Assert.Empty(agreed);
        Assert.Empty(inThePath);
    }

    [Fact]
    public async Task A_pick_on_page_three_of_a_linked_pager_lands_on_the_first_page_with_the_sort()
    {
        var page = Page.Visit("/ui-filters-tests/paged?page=3&sort=name");
        await Open(page);

        await page.Pick("Bob Ross").From("Driver");

        Assert.Equal("driver=bob&sort=name", page.TextOf("#at"));
    }

    [Fact]
    public async Task A_badge_taken_off_and_Clear_all_take_the_page_out_and_leave_the_sort()
    {
        FilteredOrders.Tag = "Urgent";
        var removed = Page.Visit("/ui-filters-tests/paged?driver=ada&page=3&sort=name");
        var cleared = Page.Visit("/ui-filters-tests/paged?driver=ada&page=3&sort=name");

        await removed.On("[aria-label=\"Remove filter Tag\"]").Click();
        FilteredOrders.Tag = "Urgent";
        await cleared.Click("Clear all");

        Assert.Equal("driver=ada&sort=name", removed.TextOf("#at"));
        Assert.Equal("sort=name", cleared.TextOf("#at"));
        FilteredOrders.Tag = null;
    }

    [Fact]
    public async Task A_pick_tells_a_pager_of_buttons_it_is_on_page_one()
    {
        var page = Page.Visit("/ui-filters-tests/turned");
        await Open(page);

        await page.Pick("Ada Lovelace").From("Driver");

        Assert.Equal("driver=ada", page.TextOf("#at"));
        Assert.Equal("1", page.TextOf("#page"));
    }

    [Fact]
    public async Task Turning_the_pager_itself_keeps_the_filters_and_the_page_it_chose()
    {
        var page = Page.Visit("/ui-filters-tests/turned?driver=ada");

        await page.Click("4");

        Assert.Equal("4", page.TextOf("#page"));
        Assert.Equal("driver=ada", page.TextOf("#at"));
    }
}
