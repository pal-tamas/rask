using System.Globalization;
using System.Text.Json;
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
///     A table drawn from a query (<c>Ui.Table.Of(query)</c>), in a real browser, paint by paint: a page change, a
///     sort, a filter and a tab show the rows, then skeleton rows of exactly their size from the very press, then the
///     new rows — the headings never leave, no paint is empty, and nothing under the table moves.
/// </summary>
/// <remarks>
///     Every paint is a <c>requestAnimationFrame</c> sample of the table: what a reader can see, frame by frame. The
///     read is held by the test (<see cref="HeldRead" />) for as long as it takes to see the skeleton, so a slow link
///     is the case under test whatever the machine. The table is the drivers list the TA app reported: 1200 drivers,
///     15 a page.
/// </remarks>
public sealed class TableSkeletonTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Pager = "[data-ui-pagination]";

    [Fact]
    public async Task A_page_change_shows_rows_then_skeleton_rows_of_their_size_then_the_next_rows()
    {
        var sent = new List<string>();
        await using var session = await OpenAsync("/", sent: sent);
        Forget(sent);

        var paints = await PressAsync(session.Page, () => session.Page.Locator($"{Pager} button:visible", new() { HasTextString = "2" }).First.ClickAsync(), "driver 16");

        AssertRowsSkeletonRows(paints, "driver 1 ", "driver 16");
        Assert.Equal(1, Pressed(sent, "\"type\":\"click\""));
    }

    [Fact]
    public async Task A_page_change_by_link_shows_the_skeleton_at_the_press_and_keeps_the_headings()
    {
        var sent = new List<string>();
        await using var session = await OpenAsync("/?links=1", sent: sent);
        Forget(sent);

        var paints = await PressAsync(session.Page, () => session.Page.Locator($"{Pager} a:visible", new() { HasTextString = "2" }).First.ClickAsync(), "driver 16");

        AssertRowsSkeletonRows(paints, "driver 1 ", "driver 16");
        Assert.Equal(1, Pressed(sent, "\"type\":\"navigate\""));
    }

    [Fact]
    public async Task A_sort_shows_rows_then_skeleton_rows_of_their_size_then_the_sorted_rows()
    {
        await using var session = await OpenAsync("/");

        var paints = await PressAsync(session.Page, () => session.Page.ClickAsync("#by-name button"), "driver 1200");

        AssertRowsSkeletonRows(paints, "driver 1 ", "driver 1200");
    }

    [Fact]
    public async Task A_filter_naming_the_table_shows_rows_then_skeleton_rows_of_their_size_then_the_filtered_rows()
    {
        await using var session = await OpenAsync("/");

        var paints = await PressAsync(session.Page, () => session.Page.SelectOptionAsync("#fuel", "adblue"), "adblue driver 1 ");

        AssertRowsSkeletonRows(paints, "driver 1 ", "adblue driver 1 ");
    }

    [Fact]
    public async Task A_tab_above_the_table_shows_rows_then_skeleton_rows_of_their_size_then_the_tabs_rows()
    {
        await using var session = await OpenAsync("/");

        var paints = await PressAsync(session.Page, () => session.Page.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Inaktív (552)" }).ClickAsync(), "inactive driver 1 ");

        AssertRowsSkeletonRows(paints, "driver 1 ", "inactive driver 1 ");
    }

    [Fact]
    public async Task A_tab_panel_whose_table_mounts_shows_its_headings_and_skeleton_rows_at_once_and_its_rows_after()
    {
        await using var session = await OpenAsync("/", record: false);
        var page = session.Page;
        await page.EvaluateAsync(Recorder, "#vehicles");
        TableSkeletonPage.Gate.Hold();

        await page.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Járművek" }).ClickAsync();
        await TableSkeletonPage.Gate.ReadStarted();
        await Expect(page.Locator("#vehicles [data-ui-skeleton-line]").First).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(300);
        TableSkeletonPage.Gate.Release();
        await Expect(page.Locator("#vehicles tbody tr").First).ToContainTextAsync("vehicle 1 ");
        await SettledAsync(page, "#vehicles");
        var paints = await PaintsAsync(page);

        var shown = paints.All.Where(paint => paint.Rows > 0).ToList();
        Assert.All(shown, paint => Assert.True(paint.Head, "a paint of the vehicles table had no headings"));
        Assert.True(shown[0].Skeleton, "the vehicles table's first paint was not its skeleton");
        Assert.Equal(TableSkeletonPage.PerPage, shown[0].Rows);
        Assert.Equal(["skeleton", "rows"], Kinds(shown, "vehicle 1 "));
    }

    // rows → skeleton → rows, each paint the same row count and height, the headings there throughout, nothing
    // under the table moved, and the skeleton already up in the task of the press itself.
    private static void AssertRowsSkeletonRows(Paints paints, string before, string after)
    {
        var first = paints.All[0];
        Assert.True(paints.AtPress?.Skeleton, "the skeleton was not up in the task of the press");
        Assert.False(paints.HeadLost, "the table's headings were taken out of the page");
        Assert.All(paints.All, paint =>
        {
            Assert.True(paint.Head, "a paint had no headings");
            Assert.Equal(TableSkeletonPage.PerPage, paint.Rows);
            Assert.InRange(paint.Height, first.Height - 1, first.Height + 1);
            Assert.InRange(paint.Below, first.Below - 1, first.Below + 1);
        });
        Assert.Equal(["rows", "skeleton", "rows"], Kinds(paints.All, before, after));
    }

    private static List<string> Kinds(IReadOnlyList<Paint> paints, params string[] rows)
    {
        var kinds = new List<string>();
        foreach (var paint in paints)
        {
            var kind = paint.Skeleton ? "skeleton" : rows.Any(text => paint.First.Contains(text, StringComparison.Ordinal)) ? "rows" : "other: " + paint.First;
            if (kinds.Count == 0 || kinds[^1] != kind)
            {
                kinds.Add(kind);
            }
        }

        return kinds;
    }

    // Presses with the read held, waits out a few paints of the skeleton, lets the read go and waits for `arrives`.
    private static async Task<Paints> PressAsync(IPage page, Func<Task> press, string arrives)
    {
        TableSkeletonPage.Gate.Hold();
        await press();
        await TableSkeletonPage.Gate.ReadStarted();
        await page.WaitForTimeoutAsync(400);
        TableSkeletonPage.Gate.Release();
        await Expect(page.Locator("#drivers tbody tr").First).ToContainTextAsync(arrives);
        await SettledAsync(page, "#drivers");
        return await PaintsAsync(page);
    }

    // The table's rows are its own again (no skeleton, from the browser or the render), and two paints have shown it.
    private static async Task SettledAsync(IPage page, string table)
    {
        await page.WaitForFunctionAsync(
            "t => !document.querySelector(t).closest('[data-skeleton], [data-rask-skeleton=loading]')", table);
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
    }

    private static async Task<Paints> PaintsAsync(IPage page) =>
        JsonSerializer.Deserialize<Paints>(await page.EvaluateAsync<string>("() => JSON.stringify(window.__paints)"), Json)!;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static void Forget(List<string> sent)
    {
        lock (sent)
        {
            sent.Clear();
        }
    }

    // How many frames the page sent carrying `kind`.
    private static int Pressed(List<string> sent, string kind)
    {
        lock (sent)
        {
            return sent.Count(frame => frame.Contains(kind, StringComparison.Ordinal));
        }
    }

    // Opens the page, and from the first frame keeps what it sends in `sent`.
    private async Task<HookSession> OpenAsync(string path, bool record = true, List<string>? sent = null)
    {
        TableSkeletonPage.Gate.Release();
        var session = await HookSession.OpenAsync<TableSkeletonPage>(playwright, path: path, beforeLoad: page =>
        {
            page.WebSocket += (_, socket) => socket.FrameSent += (_, frame) =>
            {
                if (sent is not null)
                {
                    lock (sent)
                    {
                        sent.Add(frame.Text ?? string.Empty);
                    }
                }
            };
            return Task.CompletedTask;
        }, services: services =>
        {
            services.AddRaskQuery();
            services.AddSingleton<IDispatcher, NothingToAsk>();
        });
        var page = session.Page;
        await Expect(page.Locator("#drivers tbody tr").First).ToContainTextAsync("driver 1 ");
        await page.WaitForFunctionAsync("() => (window.__raskHookSeam?.quiet?.length ?? 0) > 0");
        if (record)
        {
            await page.EvaluateAsync(Recorder, "#drivers");
        }

        return session;
    }

    // Samples the table at every paint, and once more in the task of a press, after every listener of the page's.
    private const string Recorder =
        """
        at => {
            const box = () => { const table = document.querySelector(at); return table && table.closest('[data-rask-skeleton]'); };
            const below = document.getElementById('below');
            let head = null;
            const paint = () => {
                const b = box();
                const body = b && b.querySelector('tbody');
                if (!body) return { rows: 0, skeleton: false, height: 0, below: below.getBoundingClientRect().top + scrollY, first: '', head: false };
                const thead = b.querySelector('thead');
                if (thead && !head) head = thead;
                const rows = body.querySelectorAll(':scope > tr');
                return {
                    rows: rows.length,
                    skeleton: b.hasAttribute('data-skeleton') || b.getAttribute('data-rask-skeleton') === 'loading' || !!body.querySelector('[data-ui-skeleton-line]'),
                    height: body.getBoundingClientRect().height,
                    below: below.getBoundingClientRect().top + scrollY,
                    first: rows.length ? rows[0].textContent : '',
                    head: !!thead && thead === head,
                };
            };
            window.__paints = { all: [paint()], atPress: null, headLost: false };
            const tick = () => { window.__paints.all.push(paint()); requestAnimationFrame(tick); };
            requestAnimationFrame(tick);
            const pressed = () => { window.__paints.atPress = paint(); };
            window.addEventListener('click', pressed);
            window.addEventListener('change', pressed);
            new MutationObserver(() => { if (head && !head.isConnected) window.__paints.headLost = true; })
                .observe(document.body, { subtree: true, childList: true });
        }
        """;

    private sealed record Paint(int Rows, bool Skeleton, double Height, double Below, string First, bool Head);

    private sealed record Paints(List<Paint> All, Paint? AtPress, bool HeadLost);

    // A query cache needs a dispatcher. These queries read through their own lambdas.
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

/// <summary>
///     The TA app's drivers list: 1200 drivers, 15 a page, a pager (handler or links), a sortable name, a fuel
///     filter naming the table, two tabs above it, and a tab group whose second panel mounts a vehicles table.
/// </summary>
public sealed partial class TableSkeletonPage : Component
{
    internal const int PerPage = 15;
    private const int Total = 1200;

    private readonly RouteState _route;
    private readonly Query<string[]> _drivers;
    private int _page = 1;
    private bool _desc;
    private readonly TableSkeletonFilter _filter = new();
    private string _tab = "active";
    private string _section = "drivers";

    public TableSkeletonPage(IQueryClient queries, RouteState route)
    {
        _route = route;
        _drivers = queries.Query(QueryKey.Of("drivers"), () => (Page, _desc, _filter.Fuel, _tab), (asked, _) => Read(asked.Item1, asked.Item2, asked.Item3, asked.Item4));
    }

    /// <summary>What every read here awaits.</summary>
    internal static HeldRead Gate { get; } = new();

    protected override Component? HeadAssets => [Markup.Title["table skeleton"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    private bool Links => _route.Query["links"] == "1";

    private int Page => Links && int.TryParse(_route.Query["page"], CultureInfo.InvariantCulture, out var page) ? page : _page;

    protected override Component? Render() =>
    [
        Ui.Tabs.Value(_section).OnChange(section => _section = section)[
            Ui.Tab.Name("drivers")["Sofőrök"], Ui.Tab.Name("vehicles")["Járművek"]
        ],
        _section == "vehicles"
            ? Div[TableSkeletonVehicles, Div.Id("below")["below the table"]]
            : Div[
                Select.Bind(() => _filter.Fuel).Live().Id("fuel").Aria("controls", "drivers")[
                    Option.Value("all")["All"], Option.Value("adblue")["AdBlue"]
                ],
                Ui.Tabs.Value(_tab).OnChange(tab => _tab = tab)[
                    Ui.Tab.Name("active")["Aktív (696)"], Ui.Tab.Name("inactive")["Inaktív (552)"]
                ],
                Ui.Table.Id("drivers").Of(_drivers).Class("w-full").Paginate(Pager())[
                    Ui.TableColumns[
                        Ui.TableColumn.Id("by-name").Class("w-80").Sortable(true).Sorted(true).OnSort(() => _desc = !_desc)["Name"],
                        Ui.TableColumn.Class("w-40")["Fuel"],
                        Ui.TableColumn.End["Kilometres"]
                    ],
                    Ui.TableRows[
                        (_drivers.Data ?? []).Select(name => Ui.TableRow.Key(name)[
                            Ui.TableCell[name + " "], Ui.TableCell[_filter.Fuel], Ui.TableCell.End["1 000"]
                        ])
                    ]
                ],
                Div.Id("below")["below the table"]
            ],
    ];

    private UiPagination Pager()
    {
        var pager = Ui.Pagination.Paginator(new UiPaginator { Page = Page, PerPage = PerPage, Total = Total });
        return Links ? pager.Href(page => new RouteUrl("/", $"?links=1&page={page}", typeof(TableSkeletonPage))) : pager.OnPage(page => _page = page);
    }

    private static async Task<string[]> Read(int page, bool desc, string fuel, string tab)
    {
        await Gate.Read();
        var prefix = (fuel == "adblue" ? "adblue " : "") + (tab == "inactive" ? "inactive " : "");
        return
        [
            .. Enumerable.Range(((page - 1) * PerPage) + 1, PerPage)
                .Select(n => desc ? Total + 1 - n : n)
                .Select(n => $"{prefix}driver {n.ToString(CultureInfo.InvariantCulture)}"),
        ];
    }
}

/// <summary>What the fuel filter binds.</summary>
internal sealed class TableSkeletonFilter
{
    public string Fuel { get; set; } = "all";
}

/// <summary>The second tab's table: its query is first read when the panel mounts.</summary>
public sealed partial class TableSkeletonVehicles : Component
{
    private readonly Query<string[]> _vehicles;

    public TableSkeletonVehicles(IQueryClient queries) =>
        _vehicles = queries.Query(QueryKey.Of("vehicles"), async _ =>
        {
            await TableSkeletonPage.Gate.Read();
            return Enumerable.Range(1, TableSkeletonPage.PerPage).Select(n => $"vehicle {n.ToString(CultureInfo.InvariantCulture)} ").ToArray();
        });

    protected override Component? Render() =>
        Ui.Table.Id("vehicles").Of(_vehicles).Class("w-full")[
            Ui.TableColumns[Ui.TableColumn["Plate"], Ui.TableColumn["Kilometres"]],
            Ui.TableRows[(_vehicles.Data ?? []).Select(plate => Ui.TableRow.Key(plate)[Ui.TableCell[plate], Ui.TableCell["1 000"]])]
        ];
}
