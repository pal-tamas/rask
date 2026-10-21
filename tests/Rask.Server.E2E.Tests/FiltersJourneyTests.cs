using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Cqrs;
using Rask.Querying;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A list's filters as a reader uses them: the panel opened by its button, a pick and a typed search applied as
///     they are made, each said by a badge above the list, one taken off by its cross and the rest by "Clear all" —
///     and the address saying all of it, the list's own sort kept throughout.
/// </summary>
public sealed partial class FiltersJourneyTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Panel = "dialog[data-ui-flyout]";

    // Whether the table's rows were skeleton rows in the task of the reader's press, after every listener of the page's.
    private const string WatchThePress =
        """
        () => {
            window.__skeletonAtPress = false;
            const skeleton = () => !!document.querySelector('[data-rask-skeleton][data-skeleton], [data-rask-skeleton] [data-skeleton]');
            const pressed = () => { window.__skeletonAtPress = window.__skeletonAtPress || skeleton(); };
            window.addEventListener('click', pressed);
            window.addEventListener('change', pressed);
        }
        """;

    private Task<HookSession> OpenAsync(string path) =>
        HookSession.OpenAsync<FiltersJourneyApp>(playwright, path: path, services: services =>
        {
            services.AddRaskQuery();
            services.AddSingleton<IDispatcher, FiltersJourneyPage.NothingToAsk>();
        });

    private static async Task<bool> SkeletonAtPressAsync(IPage page, Func<Task> press)
    {
        await page.EvaluateAsync(WatchThePress);
        await press();
        return await page.EvaluateAsync<bool>("() => window.__skeletonAtPress");
    }

    private static ILocator Badge(IPage page, string words) =>
        page.Locator("[data-ui-filters-badges] [data-ui-badge]").Filter(new() { HasTextString = words });

    [Fact]
    public async Task A_reader_filters_the_list_takes_one_filter_off_and_clears_the_rest_while_the_sort_stays()
    {
        await using var session = await OpenAsync("/filters-journey?sort=name");
        var page = session.Page;
        await Expect(page.Locator(Panel + " [data-ui-select-button]")).ToHaveCountAsync(0);

        await page.ClickAsync("[data-ui-filters] [data-ui-modal-trigger] button");
        await page.Locator(Panel + " [data-ui-select-button]").ClickAsync();
        await page.Locator(Panel + " [data-ui-option]").Filter(new() { HasTextString = "Bob Ross" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"driver=bob"));
        await page.FillAsync(Panel + " #f-search", "tea");
        await Expect(page).ToHaveURLAsync(new Regex(@"q=tea"));
        await page.Locator(Panel + " button").Filter(new() { HasTextString = "Close" }).ClickAsync();

        await Expect(page.Locator(Panel)).Not.ToHaveAttributeAsync("open", "");
        await Expect(Badge(page, "Driver: Bob Ross")).ToBeVisibleAsync();
        await Expect(Badge(page, "Search: tea")).ToBeVisibleAsync();
        await Expect(page.Locator("#rows")).ToHaveTextAsync("bob · tea · name");
        await Expect(page.Locator("[data-ui-filters] [data-ui-modal-trigger] [data-ui-badge]")).ToHaveTextAsync("2");

        await page.ClickAsync("[aria-label='Remove filter Driver']");
        await Expect(Badge(page, "Driver: Bob Ross")).ToHaveCountAsync(0);
        Assert.Contains("q=tea", page.Url, StringComparison.Ordinal);
        Assert.Contains("sort=name", page.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("driver=", page.Url, StringComparison.Ordinal);

        await page.ClickAsync("[data-ui-filters] [data-ui-modal-trigger] button");
        await page.Locator(Panel + " [data-ui-select-button]").ClickAsync();
        await page.Locator(Panel + " [data-ui-option]").Filter(new() { HasTextString = "Ada Lovelace" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"driver=ada"));
        await page.Locator(Panel + " button").Filter(new() { HasTextString = "Close" }).ClickAsync();
        await page.Locator("[data-ui-filters-badges] button").Filter(new() { HasTextString = "Clear all" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex(@"/filters-journey\?sort=name$"));
        await Expect(page.Locator("[data-ui-filters-badges]")).ToHaveCountAsync(0);
        await Expect(page.Locator("#rows")).ToHaveTextAsync("anyone · · name");
    }

    [Fact]
    public async Task A_shared_address_opens_on_its_filters()
    {
        await using var session = await OpenAsync("/filters-journey?driver=ada&period=2026-10-01..2026-10-09&sort=name");
        var page = session.Page;

        var badges = await page.Locator("[data-ui-filters-badges] [data-ui-badge]").AllTextContentsAsync();

        Assert.Equal(["Driver: Ada Lovelace", "Period: Oct 1, 2026 – Oct 9, 2026"], badges.Select(text => text.Trim()));
        await Expect(page.Locator("#rows")).ToHaveTextAsync("ada · · name");
    }

    [Fact]
    public async Task A_pick_and_a_badge_taken_off_turn_the_rows_into_skeleton_rows_in_the_task_of_the_press()
    {
        await using var session = await OpenAsync("/filters-journey?driver=ada&sort=name");
        var page = session.Page;
        await Expect(page.Locator("[data-rask-skeleton] table tbody tr").First).ToContainTextAsync("order for ada");
        var table = await page.Locator("[data-rask-skeleton] table").GetAttributeAsync("id");
        await page.ClickAsync("[data-ui-filters] [data-ui-modal-trigger] button");
        await page.Locator(Panel + " [data-ui-select-button]").ClickAsync();
        var fields = await page.Locator(Panel + " .space-y-6 > .space-y-6").GetAttributeAsync("aria-controls");

        var atPick = await SkeletonAtPressAsync(page, () => page.Locator(Panel + " [data-ui-option]").Filter(new() { HasTextString = "Bob Ross" }).ClickAsync());
        await Expect(page.Locator("[data-rask-skeleton] table tbody tr").First).ToContainTextAsync("order for bob");
        await page.Locator(Panel + " button").Filter(new() { HasTextString = "Close" }).ClickAsync();
        var atCross = await SkeletonAtPressAsync(page, () => page.ClickAsync("[aria-label='Remove filter Driver']"));
        await Expect(page.Locator("[data-rask-skeleton] table tbody tr").First).ToContainTextAsync("order for anyone");

        Assert.True(atPick, "the rows were not skeleton rows in the task of the pick");
        Assert.True(atCross, "the rows were not skeleton rows in the task of the badge's cross");
        Assert.NotNull(table);
        Assert.Equal(table, fields);
    }
}

/// <summary>The app: its router, and nothing else.</summary>
public sealed partial class FiltersJourneyApp : Component
{
    protected override Component? HeadAssets => [Markup.Title["filters"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

/// <summary>An orders list filtered by its address: a driver, a period and a search, and a sort that is no filter.</summary>
[Route("/filters-journey")]
public sealed partial class FiltersJourneyPage(IQueryClient queries) : Component
{
    [QueryParam("driver")] public string? Driver { get; set; }

    [QueryParam("period")] public UiDateRange? Period { get; set; }

    [QueryParam("q")] public string? Search { get; set; }

    [QueryParam("sort")] public string? Sort { get; set; }

    protected override Component? Render()
    {
        var filters = Ui.Filters[
            Ui.Select.Bind(() => Driver).Label("Driver").Listbox.Clearable().Placeholder("Anyone")[
                Ui.SelectOption.Value("ada")["Ada Lovelace"],
                Ui.SelectOption.Value("bob")["Bob Ross"]
            ],
            Ui.DatePicker.Range.Bind(() => Period).Label("Period").Locale("en-US").Clearable(),
            Ui.Input.Bind(() => Search).Label("Search")
        ];

        return Main.Class("p-6 space-y-4")[
            filters,
            filters.Badges,
            P.Id("rows")[string.Join(" · ", Driver ?? "anyone", Search ?? string.Empty, Sort ?? string.Empty)],
            P.Id("count")[filters.Active.ToString(CultureInfo.InvariantCulture)],
            Ui.Table.Of(Orders).Class("w-full")[
                Ui.TableColumns[Ui.TableColumn["Order"]],
                Ui.TableRows[(Orders.Data ?? []).Select(order => Ui.TableRow.Key(order)[Ui.TableCell[order]])]
            ]
        ];
    }

    // The rows, read again — slowly enough to be seen — whenever the address changes what they are filtered by.
    private Query<string[]> Orders => field ??= queries.Query(
        QueryKey.Of("filters-journey"), () => (Driver, Search), async (asked, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
            return Enumerable.Range(1, 3).Select(n => $"order for {asked.Driver ?? "anyone"} {n}").ToArray();
        });

    /// <summary>A query cache needs a dispatcher; these rows are read by their own lambda.</summary>
    internal sealed class NothingToAsk : IDispatcher
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
