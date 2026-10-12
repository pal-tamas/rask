using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A table whose next page is slow, in a real browser: the rows the reader has stay, dimmed, until the new
///     ones arrive — and a page that is back in time changes nothing but its rows.
/// </summary>
/// <remarks>
///     The read is held and released by the test, and the page's own clock is the test's
///     (<see cref="PageClock" />), so the 200 ms the runtime waits before it marks anything is exactly 200 of
///     the page's milliseconds however slow the machine. A CSS transition runs on the browser's own timeline,
///     which nothing here stops: the dimmed opacity is waited for, and never timed.
/// </remarks>
public sealed class PagingDimsItsRowsTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Rows = "[data-ui-rows]";
    private const string Pager = "[data-ui-pagination]";

    // The handler's read is held far longer than the default budget, so the page is sent at the budget.
    private static readonly LocatorAssertionsToHaveCSSOptions Faded = new() { Timeout = 10_000 };

    // Bounds a hang, never the work.
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_slow_next_page_keeps_every_row_and_dims_them_from_200_ms_until_the_new_rows_arrive()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        PagingDimPage.Gate.Hold();

        await page.Locator($"{Pager} button:visible", new() { HasTextString = "2" }).First.ClickAsync();
        await PagingDimPage.Gate.ReadStarted();
        await page.Clock.RunForAsync(199);
        var justBefore = (Marked: await MarkedAsync(page, Pager), Opacity: await OpacityAsync(page));
        await page.Clock.RunForAsync(2);
        await Expect(page.Locator(Pager)).ToHaveAttributeAsync("data-loading", "");
        await Expect(page.Locator(Rows)).ToHaveCSSAsync("opacity", "0.5", Faded);
        var whileHeld = (Busy: await page.Locator(Pager).GetAttributeAsync("aria-busy"), First: await FirstRowAsync(page));
        PagingDimPage.Gate.Release();
        await Expect(page.Locator($"{Rows} tr").First).ToContainTextAsync("order 2-1");

        Assert.Equal((false, "1"), justBefore);
        Assert.Null(whileHeld.Busy);
        Assert.Contains("order 1-1", whileHeld.First, StringComparison.Ordinal);
        await Expect(page.Locator(Pager)).Not.ToHaveAttributeAsync("data-loading", "");
        await Expect(page.Locator(Rows)).ToHaveCSSAsync("opacity", "1", Faded);
        Assert.Equal(PagingDimPage.PerPage, await page.EvaluateAsync<int>("() => window.__fewestRows"));
    }

    [Fact]
    public async Task A_slow_sort_dims_the_rows_by_its_heading_and_the_heading_says_it_is_busy()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        PagingDimPage.Gate.Hold();

        await page.ClickAsync("#by-name button");
        await PagingDimPage.Gate.ReadStarted();
        await page.Clock.RunForAsync(199);
        var opacityJustBefore = await OpacityAsync(page);
        await page.Clock.RunForAsync(2);
        await Expect(page.Locator("#by-name")).ToHaveAttributeAsync("data-loading", "");
        await Expect(page.Locator("#by-name")).ToHaveAttributeAsync("aria-busy", "true");
        await Expect(page.Locator(Rows)).ToHaveCSSAsync("opacity", "0.5", Faded);
        PagingDimPage.Gate.Release();
        await Expect(page.Locator($"{Rows} tr").First).ToContainTextAsync("sorted");

        Assert.Equal("1", opacityJustBefore);
        await Expect(page.Locator("#by-name")).Not.ToHaveAttributeAsync("data-loading", "");
        await Expect(page.Locator(Rows)).ToHaveCSSAsync("opacity", "1", Faded);
        Assert.Equal(PagingDimPage.PerPage, await page.EvaluateAsync<int>("() => window.__fewestRows"));
    }

    [Fact]
    public async Task A_next_page_that_is_back_in_time_never_marks_the_pager_and_never_fades_a_row()
    {
        var acked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await OpenAsync(acked);
        var page = session.Page;

        await page.Locator($"{Pager} button:visible", new() { HasTextString = "2" }).First.ClickAsync();
        await Expect(page.Locator($"{Rows} tr").First).ToContainTextAsync("order 2-1");
        await acked.Task.WaitAsync(Hang, TestContext.Current.CancellationToken);

        // The press has been answered, so the second of page time that follows has no mark left to make.
        await page.Clock.RunForAsync(1_000);
        Assert.False(await page.EvaluateAsync<bool>("() => window.__everMarked"));
        Assert.False(await page.EvaluateAsync<bool>("() => window.__everFaded"));
        Assert.Equal("1", await OpacityAsync(page));
    }

    [Fact]
    public async Task A_slow_row_action_marks_its_own_button_and_leaves_the_rows_as_they_are()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        PagingDimPage.Gate.Hold();

        await page.ClickAsync("#delete-1");
        await PagingDimPage.Gate.ReadStarted();
        await page.Clock.RunForAsync(1_000);
        await Expect(page.Locator("#delete-1")).ToHaveAttributeAsync("data-loading", "");
        var whileHeld = (
            Pager: await MarkedAsync(page, Pager),
            Opacity: await OpacityAsync(page),
            Faded: await page.EvaluateAsync<bool>("() => window.__everFaded"));
        PagingDimPage.Gate.Release();

        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=1");
        Assert.Equal((false, "1", false), whileHeld);
    }

    [Fact]
    public async Task Rows_whose_read_never_comes_back_stop_being_dimmed_at_the_cap_and_are_replaced_when_it_does()
    {
        PagingDimPage.Gate.Release();
        await using var session = await HookSession.OpenAsync<PagingDimChildPage>(playwright, beforeLoad: PageClock.Install);
        var page = session.Page;
        await Expect(page.Locator($"{Rows} tr").First).ToContainTextAsync("order 1-1");
        await PageClock.StopAsync(page);
        PagingDimPage.Gate.Hold();

        await page.Locator($"{Pager} button:visible", new() { HasTextString = "2" }).First.ClickAsync();
        await PagingDimPage.Gate.ReadStarted();
        await page.Clock.RunForAsync(201);
        await Expect(page.Locator(Rows)).ToHaveCSSAsync("opacity", "0.5", Faded);

        // The page's clock is stopped, so no client backstop can end the mark: only the server's word does,
        // two seconds after the frame that went out without the rows.
        await Expect(page.Locator(Pager)).Not.ToHaveAttributeAsync("data-loading", "", new() { Timeout = 15_000 });
        await Expect(page.Locator(Rows)).ToHaveCSSAsync("opacity", "1", Faded);
        var atTheCap = await FirstRowAsync(page);
        PagingDimPage.Gate.Release();

        await Expect(page.Locator($"{Rows} tr").First).ToContainTextAsync("order 2-1");
        Assert.Contains("order 1-1", atTheCap, StringComparison.Ordinal);
    }

    // Opens the page with its clock in the test's hands and stopped, and the observers every journey reads.
    private async Task<HookSession> OpenAsync(TaskCompletionSource? acked = null)
    {
        PagingDimPage.Gate.Release();
        var session = await HookSession.OpenAsync<PagingDimPage>(playwright, beforeLoad: page =>
        {
            page.WebSocket += (_, socket) => socket.FrameReceived += (_, frame) =>
            {
                if (frame.Text?.StartsWith("{\"type\":\"ack\"", StringComparison.Ordinal) == true)
                {
                    acked?.TrySetResult();
                }
            };
            return PageClock.Install(page);
        });
        var page = session.Page;
        await Expect(page.Locator($"{Rows} tr").First).ToContainTextAsync("order 1-1");
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");
        await page.EvaluateAsync(
            """
            () => {
                const rows = () => document.querySelectorAll('[data-ui-rows] tr').length;
                window.__fewestRows = rows();
                window.__everMarked = false;
                window.__everFaded = false;
                new MutationObserver(() => {
                    window.__fewestRows = Math.min(window.__fewestRows, rows());
                    if (document.querySelector('[data-ui-pagination][data-loading], [data-ui-column][data-loading]')) window.__everMarked = true;
                }).observe(document.body, { attributes: true, subtree: true, childList: true });
                document.addEventListener('transitionrun', e => {
                    if (e.target.matches && e.target.matches('[data-ui-rows]')) window.__everFaded = true;
                });
            }
            """);
        await PageClock.StopAsync(page);
        return session;
    }

    private static Task<bool> MarkedAsync(IPage page, string selector) =>
        page.EvaluateAsync<bool>("s => document.querySelector(s).hasAttribute('data-loading')", selector);

    private static Task<string> OpacityAsync(IPage page) =>
        page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('[data-ui-rows]')).opacity");

    private static async Task<string> FirstRowAsync(IPage page) =>
        await page.Locator($"{Rows} tr").First.TextContentAsync() ?? string.Empty;
}

/// <summary>
///     Five orders a page, a pager under them, a heading that sorts and a Delete in every row. Each of the three
///     awaits the gate, which a test holds to make the read slow.
/// </summary>
public sealed partial class PagingDimPage : Component
{
    internal const int PerPage = 5;

    private int _page = 1;
    private int _shown = 1;
    private bool _sorted;
    private bool _shownSorted;
    private bool _ready;
    private int _deleted;

    /// <summary>What every handler here awaits.</summary>
    internal static HeldRead Gate { get; } = new();

    // The kit's sheet, which is what dims the rows.
    protected override Component? HeadAssets => [Markup.Title["paging dim"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        P.Id("is-ready")[_ready ? "ready" : "booting"],
        P.Id("deleted")[$"deleted={_deleted}"],
        Ui.Table.Paginate(Ui.Pagination.Paginator(new UiPaginator { Page = _page, PerPage = PerPage, Total = 40 }).OnPage(Load))[
            Ui.TableColumns[
                Ui.TableColumn.Id("by-name").Sortable(true).Sorted(_sorted).OnSort(Sort)["Order"],
                Ui.TableColumn.End[""]
            ],
            Ui.TableRows[
                Enumerable.Range(1, PerPage).Select(n => Ui.TableRow.Key(n)[
                    Ui.TableCell[$"order {_shown}-{n}{(_shownSorted ? " sorted" : "")}"],
                    Ui.TableCell.End[Ui.Button.Sm.Id($"delete-{n}").OnClick(Delete)["Delete"]]
                ])
            ]
        ],
    ];

    // The plain shape: the pager moves, the rows stay until the read is back.
    private async Task Load(int page)
    {
        _page = page;
        await Gate.Read();
        _shown = page;
    }

    private async Task Sort()
    {
        _sorted = true;
        await Gate.Read();
        _shownSorted = true;
    }

    private async Task Delete()
    {
        await Gate.Read();
        _deleted++;
    }
}

/// <summary>The same table, whose pager only moves the number: the rows are a child's, read again in <c>OnUpdated</c>.</summary>
public sealed partial class PagingDimChildPage : Component
{
    private int _page = 1;

    protected override Component? HeadAssets => [Markup.Title["paging dim child"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Ui.Table.Paginate(Ui.Pagination.Paginator(new UiPaginator { Page = _page, PerPage = PagingDimPage.PerPage, Total = 40 }).OnPage(page => _page = page))[
            Ui.TableColumns[Ui.TableColumn["Order"]],
            PagingDimRows.Page(_page)
        ];
}

/// <summary>One page of rows. The ones it has stay until the next are back.</summary>
public sealed partial class PagingDimRows : Component
{
    private int _shown = 1;

    public int? Page { get; set; }

    protected override async Task OnUpdated()
    {
        await PagingDimPage.Gate.Read();
        _shown = Page ?? 1;
    }

    protected override Component? Render() =>
        Ui.TableRows[
            Enumerable.Range(1, PagingDimPage.PerPage).Select(n => Ui.TableRow.Key(n)[Ui.TableCell[$"order {_shown}-{n}"]])
        ];
}

/// <summary>A read a test holds: at once unless held, and then not until it is let go.</summary>
internal sealed class HeldRead
{
    private TaskCompletionSource? _held;
    private TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Hold()
    {
        _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release() => Interlocked.Exchange(ref _held, null)?.TrySetResult();

    /// <summary>Completes once a handler is waiting on the held read. Thirty seconds bounds a hang.</summary>
    public Task ReadStarted() => _started.Task.WaitAsync(TimeSpan.FromSeconds(30));

    public Task Read()
    {
        if (_held is not { } held)
        {
            return Task.CompletedTask;
        }

        _started.TrySetResult();
        return held.Task;
    }
}
