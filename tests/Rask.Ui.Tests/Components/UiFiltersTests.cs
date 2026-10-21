using System.Globalization;
using Rask.Core;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>An orders list filtered by what its address holds, as an app writes one.</summary>
[Route("/ui-filters-tests/orders")]
public sealed partial class FilteredOrdersPage(RouteState route) : Component
{
    [QueryParam("driver")] public string? Driver { get; set; }

    [QueryParam("period")] public UiDateRange? Period { get; set; }

    [QueryParam("q")] public string? Search { get; set; }

    [QueryParam("paid")] public bool Paid { get; set; }

    [QueryParam("express")] public bool Express { get; set; }

    [QueryParam("status")] public string? Status { get; set; }

    [QueryParam("sort")] public string? Sort { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var filters = Ui.Filters.OnOpen(() => { FilteredOrders.Opened++; })[
            Ui.Select.Bind(() => Driver).Label("Driver").Placeholder("Anyone")[
                Ui.SelectOption.Value("ada")["Ada Lovelace"],
                Ui.SelectOption.Value("bob")["Bob Ross"]
            ],
            Ui.DatePicker.Range.Bind(() => Period).Label("Period").Locale("en-US").Clearable(),
            Ui.Input.Bind(() => Search).Label("Search"),
            Ui.Checkbox.Bind(() => Paid).Label("Paid only"),
            Ui.Switch.Bind(() => Express).Label("Express"),
            Ui.RadioGroup.Bind(() => Status).Label("Status")[Ui.Radio.Value("open").Label("Open"), Ui.Radio.Value("shipped").Label("Shipped")],
            Ui.Filter.Label("Tag").Shown(FilteredOrders.Tag).OnRemove(() => { FilteredOrders.Tag = null; })
        ];

        return Main[
            filters,
            filters.Badges,
            P.Id("active")[filters.Active.ToString(CultureInfo.InvariantCulture)],
            P.Id("at")[string.Join('&', route.Query.OrderBy(q => q.Key, StringComparer.Ordinal).Select(q => q.Key + "=" + q.Value))]
        ];
    }
}

/// <summary>What the orders page keeps beside its address: a filter the set cannot read, and how often it was opened.</summary>
internal static class FilteredOrders
{
    /// <summary>What the page's own escape-hatch filter holds.</summary>
    internal static string? Tag { get; set; }

    /// <summary>How many times the reader opened the panel.</summary>
    internal static int Opened { get; set; }
}

/// <summary>A filter over a member the address does not fill.</summary>
[Route("/ui-filters-tests/unkept")]
public sealed partial class UnkeptFilterPage : Component
{
    public string? Plain { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Ui.Filters[Ui.Select.Bind(() => Plain).Label("Plain")[Ui.SelectOption.Value("a")["A"]]];
}

/// <summary>
///     <c>Ui.Filters</c>: each control bound to the address, a badge for each filter in force, and a count of them.
/// </summary>
public partial class UiFiltersTests : global::Rask.Core.RaskMarkup
{
    private const string Orders = "/ui-filters-tests/orders";

    private static IReadOnlyList<string> Badges(Page page) =>
        [.. page.FindAll("[data-ui-filters-badges] [data-ui-badge]").Select(badge => string.Join(' ', badge.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))];

    private static Task Open(Page page) => page.On("dialog > input[data-rask-modal-shown]").Change("true");

    [Fact]
    public void Every_kind_of_control_says_its_filter_in_its_own_words()
    {
        FilteredOrders.Tag = "Urgent";
        var page = Page.Visit(Orders + "?driver=ada&period=2026-10-01..2026-10-09&q=%20tea%20&paid=true&express=true&status=shipped");

        var badges = Badges(page);

        Assert.Equal(
            ["Driver: Ada Lovelace", "Period: Oct 1, 2026 – Oct 9, 2026", "Search: tea", "Paid only", "Express", "Status: Shipped", "Tag: Urgent"],
            badges);
        Assert.Equal("7", page.TextOf("#active"));
        FilteredOrders.Tag = null;
    }

    [Fact]
    public void A_range_a_preset_made_is_said_by_the_presets_name()
    {
        var page = Page.Visit(Orders + "?period=2026-10-03..2026-10-09~last7Days");

        var badges = Badges(page);

        Assert.Equal(["Period: Last 7 Days"], badges);
    }

    [Fact]
    public void Nothing_set_blank_or_default_is_no_filter_and_draws_no_badges()
    {
        var page = Page.Visit(Orders + "?q=%20%20&paid=false&sort=name");

        var active = page.TextOf("#active");

        Assert.Equal("0", active);
        Assert.False(page.Exists("[data-ui-filters-badges]"));
        Assert.False(page.Exists("[data-ui-filters] [data-ui-badge]"));
    }

    [Fact]
    public void The_button_counts_the_filters_in_force()
    {
        var page = Page.Visit(Orders + "?driver=bob&paid=true");

        var button = page.TextOf("[data-ui-filters] [data-ui-modal-trigger] button");

        Assert.StartsWith("Filters", button, StringComparison.Ordinal);
        Assert.Equal("2", page.TextOf("[data-ui-filters] [data-ui-modal-trigger] button [data-ui-badge]"));
    }

    [Fact]
    public async Task Taking_one_badge_off_removes_its_key_and_keeps_the_rest_of_the_address()
    {
        var page = Page.Visit(Orders + "?driver=ada&paid=true&sort=name");

        await page.On("[aria-label=\"Remove filter Driver\"]").Click();

        Assert.Equal("paid=true&sort=name", page.TextOf("#at"));
        Assert.Equal(["Paid only"], Badges(page));
    }

    [Fact]
    public async Task Clear_all_takes_every_filter_off_and_leaves_the_sort()
    {
        FilteredOrders.Tag = "Urgent";
        var page = Page.Visit(Orders + "?driver=ada&q=tea&sort=name");

        await page.Click("Clear all");

        Assert.Equal("sort=name", page.TextOf("#at"));
        Assert.Null(FilteredOrders.Tag);
        Assert.Equal("0", page.TextOf("#active"));
    }

    [Fact]
    public void Clear_all_is_offered_from_two_filters_on()
    {
        var one = Page.Visit(Orders + "?driver=ada");
        var two = Page.Visit(Orders + "?driver=ada&paid=true");

        var offered = (one.Exists("[data-ui-filters-badges] [data-rask-key=\"clear-all\"]"), two.Exists("[data-ui-filters-badges] [data-rask-key=\"clear-all\"]"));

        Assert.Equal((false, true), offered);
    }

    [Fact]
    public async Task A_pick_in_a_select_moves_the_address_at_once()
    {
        var page = Page.Visit(Orders + "?sort=name");
        await Open(page);

        await page.Pick("Bob Ross").From("Driver");

        Assert.Equal("driver=bob&sort=name", page.TextOf("#at"));
        Assert.Equal(["Driver: Bob Ross"], Badges(page));
    }

    [Fact]
    public async Task A_range_picked_moves_the_address_as_one_key()
    {
        var page = Page.Visit(Orders);
        await Open(page);

        await page.On("[data-ui-date-picker] input[type=\"hidden\"]").Input("2026-10-01/2026-10-09");

        Assert.Equal("period=2026-10-01..2026-10-09", page.TextOf("#at"));
    }

    [Fact]
    public async Task Typing_in_a_text_filter_moves_the_address_without_waiting_for_an_action()
    {
        var page = Page.Visit(Orders);
        await Open(page);

        await page.Type("tea").Into("Search");

        Assert.Equal("q=tea", page.TextOf("#at"));
        Assert.Equal(["Search: tea"], Badges(page));
    }

    [Fact]
    public async Task Opening_the_panel_draws_its_fields_and_tells_the_page()
    {
        var page = Page.Visit(Orders);
        var closed = page.Exists("dialog [data-ui-input]");
        var before = FilteredOrders.Opened;

        await Open(page);

        Assert.False(closed);
        Assert.Equal(before + 1, FilteredOrders.Opened);
        Assert.True(page.Exists("dialog [data-ui-input]"));
        Assert.Contains("Clear filters", page.TextOf("dialog[data-modal]"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_filter_over_a_member_the_address_does_not_fill_names_the_declaration_to_write()
    {
        var failure = Assert.ThrowsAny<Exception>(() => Page.Visit("/ui-filters-tests/unkept"));

        var message = failure.ToString();

        Assert.Contains("[QueryParam(\"plain\")] public string? Plain { get; set; }", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_filter_with_no_children_is_a_badge_alone()
    {
        var filter = Ui.Filter.Label("Tag").Shown("Urgent");

        var html = filter.ToHtml();

        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void A_filter_with_a_control_draws_it_in_the_panel()
    {
        var filter = Ui.Filter.Label("Tags").Shown(null)[Ui.Input.Of<string>().Label("Tags")];

        var html = filter.ToHtml();

        Assert.Contains("data-ui-filter", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-input", html, StringComparison.Ordinal);
    }
}
