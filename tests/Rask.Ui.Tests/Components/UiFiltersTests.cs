using System.Globalization;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Cqrs;
using Rask.Querying;
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
            P.Id("at")[string.Join('&', route.Query.OrderBy(q => q.Key, StringComparer.Ordinal).Select(q => q.Key + "=" + q.Value))],
            FilteredOrders.Table("orders")
        ];
    }
}

/// <summary>Filters of one label in force together, as TA's fuelings page has them (#1257): two keyed, two not.</summary>
[Route("/ui-filters-tests/same-label")]
public sealed partial class SameLabelFiltersPage : Component
{
    /// <inheritdoc />
    protected override Component? Render()
    {
        var filters = Ui.Filters[
            Ui.Filter.Key("filter-7").Label("Vehicles").Shown("ABC-123"),
            Ui.Filter.Key("filter-8").Label("Vehicles").Shown("active only"),
            Ui.Filter.Label("Driver").Shown("Ada"),
            Ui.Filter.Label("Driver").Shown("Bob")
        ];

        return Main[filters, filters.Badges];
    }
}

/// <summary>A filter box over a <c>bool?</c> member: an absent key is the box left unticked.</summary>
[Route("/ui-filters-tests/nullable-box")]
public sealed partial class NullableBoxPage : Component
{
    [QueryParam("ibc")] public bool? Ibc { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Main[Ui.Filters[Ui.Checkbox.Bind(() => Ibc).Label("IBC only")], FilteredOrders.Table("boxes")];
}

/// <summary>A filtered list whose table has no id of its own: the set gives it one.</summary>
[Route("/ui-filters-tests/unnamed")]
public sealed partial class UnnamedTablePage : Component
{
    [QueryParam("driver")] public string? Driver { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var filters = Ui.Filters[Ui.Select.Bind(() => Driver).Label("Driver")[Ui.SelectOption.Value("ada")["Ada Lovelace"]]];

        return Main[filters, filters.Badges, FilteredOrders.Table()];
    }
}

/// <summary>What the orders page keeps beside its address: a filter the set cannot read, and how often it was opened.</summary>
internal static class FilteredOrders
{
    /// <summary>What the page's own escape-hatch filter holds.</summary>
    internal static string? Tag { get; set; }

    /// <summary>How many times the reader opened the panel.</summary>
    internal static int Opened { get; set; }

    /// <summary>The rows every filtered list here is drawn from.</summary>
    internal static readonly Query<string[]> Rows =
        new SessionQueryClient(new NothingToAsk()).Query(QueryKey.Of("filtered-orders"), _ => Task.FromResult<string[]>(["#3102"]));

    /// <summary>The list the filters drive: a table drawn from a query.</summary>
    internal static Component Table(string? id = null) =>
        Ui.Table.Id(id).Of(Rows)[
            Ui.TableColumns[Ui.TableColumn["Order"]],
            Ui.TableRows[(Rows.Data ?? []).Select(order => Ui.TableRow.Key(order)[Ui.TableCell[order]])]
        ];

    // A query cache needs a dispatcher. This query reads through its own lambda.
    private sealed class NothingToAsk : IDispatcher
    {
        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send(ICommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TEvent>(TEvent e, CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();

        public IAsyncEnumerable<TEvent> Subscribe<TEvent>(CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();

        public IAsyncEnumerable<TEvent> Subscribe<TEvent>(
            ISubscription<TEvent> subscription, CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();
    }
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
[Collection(FilteredOrdersCollection.Name)]
public partial class UiFiltersTests : global::Rask.Core.RaskMarkup
{
    // Each test starts with no escape-hatch tag: a test that set one and failed must not leave it to the next.
    public UiFiltersTests() => FilteredOrders.Tag = null;

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
    public void A_filter_box_over_a_nullable_member_is_unticked_while_its_key_is_absent()
    {
        var page = Page.Visit("/ui-filters-tests/nullable-box");

        var dashed = page.Exists("[data-ui-filters] [data-indeterminate]");

        Assert.False(dashed);
    }

    [Fact]
    public void The_button_counts_the_filters_in_force()
    {
        var page = Page.Visit(Orders + "?driver=bob&paid=true");

        var button = page.TextOf("[data-ui-filters] [data-ui-modal-trigger] button");

        Assert.StartsWith("Filters", button, StringComparison.Ordinal);
        Assert.Equal("2", page.TextOf("[data-ui-filters] [data-ui-modal-trigger] button span span"));
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
    public async Task Every_press_that_changes_the_filters_names_the_table_after_the_set()
    {
        var page = Page.Visit(Orders + "?driver=ada&paid=true");
        await Open(page);

        var named = new[]
        {
            "[aria-label=\"Remove filter Driver\"]",
            "[aria-label=\"Remove filter Paid only\"]",
            "[data-ui-filters-badges] [data-rask-key=\"clear-all\"]",
            "dialog[data-modal] .space-y-6 > .space-y-6",
        }.Select(selector => page.Find(selector).Attribute("aria-controls"));

        Assert.All(named, controls => Assert.Equal("orders", controls));
        Assert.Equal("orders", page.FindAll("dialog[data-modal] button").Single(b => b.TextContent.Contains("Clear filters", StringComparison.Ordinal)).Attribute("aria-controls"));
        Assert.True(page.Exists("table#orders"));
    }

    [Fact]
    public void A_table_with_no_id_is_given_one_and_named_by_the_badges()
    {
        var page = Page.Visit("/ui-filters-tests/unnamed?driver=ada");

        var controls = page.Find("[aria-label=\"Remove filter Driver\"]").Attribute("aria-controls");

        Assert.NotNull(controls);
        Assert.EndsWith("-table", controls, StringComparison.Ordinal);
        Assert.True(page.Exists("table#" + controls));
    }

    [Fact]
    public void Badges_of_one_label_are_told_apart_by_their_filters_keys_or_their_places()
    {
        var page = Page.Visit("/ui-filters-tests/same-label");

        var keys = page.FindAll("[data-ui-filters-badges] > [data-ui-badge]").Select(badge => badge.Attribute("data-rask-key")).ToList();

        Assert.Equal(["filter-7", "filter-8", "Driver", "Driver#3"], keys);
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
