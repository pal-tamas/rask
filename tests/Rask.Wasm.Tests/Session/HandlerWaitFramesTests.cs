using System.Text;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;

#pragma warning disable RASK014 // test components built with `new`: they have no generated entries
#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Session;

// The browser host's side of a handler's work: nothing is painted until the handler has answered. What it sets
// between its awaits is withheld while it runs, and the paint after it waits for the reads its change started,
// both within the handler budget. The read is held and released by the test, and the budget is either longer
// than a test may run or over while the read is still held.
[Collection("WasmSession")]
public sealed class HandlerWaitFramesTests : ResettingTestBase, IDisposable
{
    private static readonly TimeSpan Patient = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(40);

    private readonly List<string> _painted = [];

    public HandlerWaitFramesTests()
        : base(LiveDiffMode.DisabledFull) =>
        JSInterop.AppliedFrames = _painted;

    public void Dispose() => JSInterop.AppliedFrames = null;

    [Fact]
    public async Task Paging_by_a_handler_whose_read_is_within_the_budget_is_one_paint_with_the_pager_and_the_rows()
    {
        var app = new HeldPagingApp();
        var (session, click) = await Open(app, Patient);

        var dispatch = session.DispatchAsync(Click(click));
        await app.ReadStarted();
        app.Release();
        await dispatch;

        var paint = Assert.Single(_painted);
        Assert.Contains("pager at 2", paint, StringComparison.Ordinal);
        Assert.Contains("rows of 2", paint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paging_whose_read_outlasts_the_budget_paints_the_pager_over_the_old_rows_and_then_the_rows()
    {
        var app = new HeldPagingApp();
        var (session, click) = await Open(app, Brief);

        var dispatch = session.DispatchAsync(Click(click));
        await WaitFor.True(() => Painted() == 1, "the paint at the budget");
        var atTheBudget = _painted[0];
        var doneWhileReading = dispatch.IsCompleted;
        app.Release();
        await dispatch;

        // The dispatch's task ends with the handler here, and it is what ends the pressed control's busy mark.
        Assert.False(doneWhileReading);
        Assert.Contains("pager at 2", atTheBudget, StringComparison.Ordinal);
        Assert.Contains("rows of 1", atTheBudget, StringComparison.Ordinal);
        Assert.Contains("rows of 2", _painted[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handler_budget_of_zero_paints_what_the_handler_set_before_its_await_at_once()
    {
        var app = new HeldPagingApp();
        var (session, click) = await Open(app, TimeSpan.Zero);

        var dispatch = session.DispatchAsync(Click(click));
        await WaitFor.True(() => Painted() == 1, "the paint at the first await");
        var atOnce = _painted[0];
        app.Release();
        await dispatch;

        Assert.Contains("pager at 2", atOnce, StringComparison.Ordinal);
        Assert.Contains("rows of 1", atOnce, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_field_changed_by_a_handler_waits_for_the_child_that_reads_again_and_is_one_paint()
    {
        var app = new HeldPagingApp { RowsAreAChilds = true };
        var (session, click) = await Open(app, Patient);

        var dispatch = session.DispatchAsync(Click(click));
        await app.ReadStarted();
        var paintedWhileReading = Painted();
        app.Release();
        await dispatch;

        Assert.Equal(0, paintedWhileReading);
        Assert.Contains("pager at 2", _painted[0], StringComparison.Ordinal);
        Assert.Contains("rows of 2", _painted[0], StringComparison.Ordinal);
        Assert.True(session.ReadsSettledAsync().IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_childs_read_that_outlasts_the_budget_keeps_the_page_unsettled_until_its_rows_are_painted()
    {
        var app = new HeldPagingApp { RowsAreAChilds = true };
        var (session, click) = await Open(app, Brief);

        var atTheBudget = Encoding.UTF8.GetString(await session.DispatchAsync(Click(click)));
        var settled = session.ReadsSettledAsync();
        var settledWhileReading = settled.IsCompleted;
        app.Release();
        await settled;

        // What the browser waits on before it ends the pressed control's busy mark (rask.wasm.ts).
        Assert.False(settledWhileReading);
        Assert.Contains("pager at 2", atTheBudget, StringComparison.Ordinal);
        Assert.Contains("rows of 1", atTheBudget, StringComparison.Ordinal);
        Assert.Contains("rows of 2", Encoding.UTF8.GetString(session.LastSentFrame), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handler_with_nothing_to_wait_for_is_painted_without_waiting_however_long_the_budget()
    {
        var app = new HeldPagingApp { ReadsNothing = true };
        var (session, click) = await Open(app, Patient);

        var paint = Encoding.UTF8.GetString(await session.DispatchAsync(Click(click)));

        // The budget is longer than this test may run: a dispatch that sat it out would never return.
        Assert.Contains("pager at 2", paint, StringComparison.Ordinal);
        Assert.True(session.ReadsSettledAsync().IsCompletedSuccessfully);
    }

    private int Painted()
    {
        lock (_painted)
        {
            return _painted.Count;
        }
    }

    private async Task<(WasmLiveSession Session, string Click)> Open(HeldPagingApp app, TimeSpan budget)
    {
        var (session, _) = NewSession(_ => app, diffMode: DiffMode, handlerBudget: budget);
        var first = Encoding.UTF8.GetString(await session.InitialRenderAsync());
        var click = Regex.Match(
            first, "data-rask-on-click=..([^\"\\\\]+)", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
        _painted.Clear();
        app.Hold();
        return (session, click);
    }

    private static byte[] Click(string id) => Utf8($$"""{"id":"{{id}}","type":"click"}""");
}

// A pager over rows. The handler moves the page and awaits the read itself, or — RowsAreAChilds — only moves
// the page, and a child reads again when the number reaches it.
[SkipFactory]
internal sealed class HeldPagingApp : Component
{
    private TaskCompletionSource? _held;
    private int _started;
    private int _page = 1;
    private int _rowsOf = 1;

    public bool RowsAreAChilds { get; init; }

    public bool ReadsNothing { get; init; }

    public void Hold()
    {
        Volatile.Write(ref _started, 0);
        _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release() => Interlocked.Exchange(ref _held, null)?.TrySetResult();

    public Task ReadStarted() => WaitFor.True(() => Volatile.Read(ref _started) > 0, "the read to start");

    public Task Read()
    {
        if (_held is not { } held)
        {
            return Task.CompletedTask;
        }

        Interlocked.Increment(ref _started);
        return held.Task;
    }

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        Main[
            P[$"pager at {_page}"],
            Button.OnClick(Next)["next"],
            RowsAreAChilds ? new HeldPagingRows { App = this, Page = _page } : P[$"rows of {_rowsOf}"]
        ];

    private async Task Next()
    {
        _page++;
        if (RowsAreAChilds || ReadsNothing)
        {
            return;
        }

        await Read();
        _rowsOf = _page;
    }
}

[SkipFactory]
internal sealed class HeldPagingRows : Component
{
    private int _rowsOf = 1;

    public HeldPagingApp? App { get; set; }

    public int Page { get; set; }

    protected override async Task OnUpdated()
    {
        await App!.Read();
        _rowsOf = Page;
    }

    protected override Component? Render() => P[$"rows of {_rowsOf}"];
}
