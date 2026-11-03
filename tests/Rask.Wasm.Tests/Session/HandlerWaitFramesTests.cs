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
    public async Task A_read_that_never_comes_back_stops_holding_the_page_at_the_cap_and_its_rows_are_painted_when_it_does()
    {
        var app = new HeldPagingApp { RowsAreAChilds = true };
        var (session, click) = await Open(app, Brief);
        session.ReadsCap = TimeSpan.FromMilliseconds(40);

        await session.DispatchAsync(Click(click));
        await session.ReadsSettledAsync();
        var atTheCap = Encoding.UTF8.GetString(session.LastSentFrame);
        app.Release();
        await WaitFor.True(
            () => Encoding.UTF8.GetString(session.LastSentFrame).Contains("rows of 2", StringComparison.Ordinal),
            "the rows to be painted once the read is back");

        // The read was still held when the page stopped waiting on it: the busy mark ends there, not the read.
        Assert.Contains("rows of 1", atTheCap, StringComparison.Ordinal);
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

    [Fact]
    public async Task A_paint_while_a_handler_awaits_inside_an_ambient_scope_is_drawn_outside_that_scope()
    {
        var app = new HeldPagingApp { ReadsAcross = true };
        var (session, click) = await Open(app, Brief);

        var dispatch = session.DispatchAsync(Click(click));
        await WaitFor.True(() => Painted() == 1, "the paint at the budget");
        app.Release();
        await dispatch;

        // The handler's read ran in the scope; every paint — those between its awaits too — is the page's own.
        string[] painted;
        lock (_painted)
        {
            painted = [.. _painted];
        }

        Assert.True(painted.Length > 1, "a paint between the handler's awaits");
        Assert.DoesNotContain(painted, p => p.Contains("no partner", StringComparison.Ordinal));
        Assert.Contains("rows of 2", painted[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_busy_buttons_handler_that_only_awaits_a_read_inside_an_ambient_scope_never_paints_that_scope()
    {
        var app = new HeldPagingApp { CountsMissing = true };
        var (session, click) = await Open(app, Brief);

        var dispatch = session.DispatchAsync(Click(click));
        await WaitFor.True(() => Painted() == 1, "the paint at the budget");
        app.Release();
        await dispatch;

        // No StateHasChanged anywhere: what is painted while it waits is the budget's paint and the repaints around
        // the continuations of its reads, which the scope is open in.
        var painted = PaintedSoFar();
        Assert.DoesNotContain(painted, p => p.Contains("no partner", StringComparison.Ordinal));
        Assert.Contains("rows of 2", painted[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_redrawn_from_outside_while_a_handler_awaits_inside_an_ambient_scope_never_paints_that_scope()
    {
        using var ticker = new HeldTicker();
        var app = new HeldPagingApp { CountsMissing = true, Board = ticker };
        var (session, click) = await Open(app, Brief);

        var dispatch = session.DispatchAsync(Click(click));
        await WaitFor.True(() => Painted() == 1, "the paint at the budget");
        ticker.Start(TimeSpan.FromMilliseconds(5));
        await WaitFor.True(() => ticker.Ticks >= 3, "the board to be redrawn from outside while the read is held");
        app.Release();
        await dispatch;
        ticker.Stop();

        // The board's redraws wait for the handler, and the repaints around its continuations paint them: inside the
        // scope, as those continuations are, the board drew for no partner.
        var painted = PaintedSoFar();
        Assert.DoesNotContain(painted, p => p.Contains("no partner", StringComparison.Ordinal));
        Assert.Contains("rows of 2", painted[^1], StringComparison.Ordinal);
    }

    private string[] PaintedSoFar()
    {
        lock (_painted)
        {
            return [.. _painted];
        }
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

    // The handler reads inside an ambient scope, as Tenant.Across() opens one, through a helper that leaves its context.
    public bool ReadsAcross { get; init; }

    // The handler is one awaited call and nothing else — no StateHasChanged — and the call reads twice inside the
    // scope, as a data layer's Tenant.Across() query does.
    public bool CountsMissing { get; init; }

    // A board beside the pager, redrawn on every tick of this timer: what a page redrawn from outside looks like.
    public HeldTicker? Board { get; init; }

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        Main[
            P[HeldAcross.IsOpen ? "no partner" : "for the partner"],
            P[$"pager at {_page}"],
            Board is { } ticker ? new HeldBoard { Ticker = ticker } : null,
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

        if (CountsMissing)
        {
            _rowsOf = await MissingAt();
            return;
        }

        if (ReadsAcross)
        {
            using (HeldAcross.Enter())
            {
                await ReadLeavingTheContext();

                // Halfway: the page is drawn again by the repaint that follows this continuation, as a busy button is.
                StateHasChanged();
                await ReadLeavingTheContext();
            }
        }
        else
        {
            await Read();
        }

        _rowsOf = _page;
    }

    // The app's own awaits come back on the handler's context; the reads under them leave it, as EF's do.
    private async Task<int> MissingAt()
    {
        using (HeldAcross.Enter())
        {
            await ReadLeavingTheContext();
            await ReadLeavingTheContext();
        }

        return _page;
    }

    private async Task ReadLeavingTheContext() => await Read().ConfigureAwait(false);
}

// An ambient scope on an AsyncLocal, which nothing outside the block it is opened in may see.
internal static class HeldAcross
{
    private static readonly AsyncLocal<bool> Open = new();

    public static bool IsOpen => Open.Value;

    public static IDisposable Enter()
    {
        Open.Value = true;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Open.Value = false;
    }
}

// What redraws a page from outside every few milliseconds, as a board of pumps does while another pump talks: a timer
// of its own, never started by a handler.
internal sealed class HeldTicker : IDisposable
{
    private Timer? _timer;
    private int _ticks;

    public event EventHandler? Ticked;

    public int Ticks => Volatile.Read(ref _ticks);

    public void Start(TimeSpan every)
    {
        // Its own flow, as a service's timer has: nothing the test holds rides into its ticks.
        using (ExecutionContext.SuppressFlow())
        {
            _timer = new Timer(_ => Tick(), null, every, every);
        }
    }

    public void Stop() => Interlocked.Exchange(ref _timer, null)?.Dispose();

    public void Dispose() => Stop();

    private void Tick()
    {
        Interlocked.Increment(ref _ticks);
        Ticked?.Invoke(this, EventArgs.Empty);
    }
}

// Redrawn on every tick, and draws for no partner inside the scope, as the page does.
[SkipFactory]
internal sealed class HeldBoard : Component
{
    public HeldTicker? Ticker { get; set; }

    protected override Task OnMount()
    {
        Ticker!.Ticked += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        Ticker!.Ticked -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
        P[$"board {(HeldAcross.IsOpen ? "no partner" : "for the partner")} at tick {Ticker!.Ticks}"];
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
