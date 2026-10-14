using System.Text;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

/// <summary>
///     A session is disposed under work that is still running: a handler at an await, an event queued behind
///     it, a page being waited for. None of that work may be left waiting for good, none of it is a fault, and
///     none of it paints the page that has gone.
/// </summary>
/// <remarks>
///     The server host's <c>DisposedSessionTests</c>, for the browser's session. Nothing in a browser disposes
///     the session today, so these pin the teardown for the hosts that do: the tests, and a second boot.
/// </remarks>
[Collection("WasmSession")]
public sealed class DisposedSessionTests : ResettingTestBase, IDisposable
{

    private static readonly TimeSpan Patient = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(40);

    private readonly List<string> _painted = [];

    public DisposedSessionTests()
        : base(LiveDiffMode.DisabledFull) =>
        JSInterop.AppliedFrames = _painted;

    public void Dispose() => JSInterop.AppliedFrames = null;

    [Fact]
    public async Task A_handler_still_running_ends_without_a_fault_once_its_session_is_disposed()
    {
        var app = new HeldPagingApp();
        var (session, click) = await Open(app, TimeSpan.Zero);
        var dispatch = session.DispatchAsync(Click(click));
        await app.ReadStarted();
        await WaitFor.True(() => Painted() == 1, "the paint at the handler's first await");
        session.Dispose();

        app.Release();
        var answer = await dispatch.WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.Empty(answer);
        Assert.Equal(1, Painted());
    }

    [Fact]
    public async Task An_event_waiting_behind_a_handler_ends_when_the_handler_does_though_the_session_was_disposed()
    {
        var app = new HeldPagingApp();
        var (session, click) = await Open(app, TimeSpan.Zero);
        var running = session.DispatchAsync(Click(click));
        await app.ReadStarted();
        await WaitFor.True(() => Painted() == 1, "the paint at the handler's first await");
        var waiting = session.DispatchAsync(Click(click));
        session.Dispose();

        app.Release();
        var answer = await waiting.WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);
        await running.WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.Empty(answer);
        Assert.Equal(1, Painted());
    }

    [Fact]
    public async Task A_render_asked_of_a_disposed_session_is_nothing()
    {
        var (session, _) = await Open(new HeldPagingApp(), TimeSpan.Zero);
        session.Dispose();

        await session.RequestRender().WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.Equal(0, Painted());
    }

    [Fact]
    public async Task The_repaint_of_a_handler_still_running_is_nothing_once_its_session_is_disposed()
    {
        var (session, _) = await Open(new HeldPagingApp(), TimeSpan.Zero);
        session.Dispose();

        await ((IRenderHandle)session).RenderInScopeAsync()
            .WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.Equal(0, Painted());
    }

    [Fact]
    public async Task An_event_sent_to_a_disposed_session_runs_no_handler_and_paints_nothing()
    {
        var (session, click) = await Open(new HeldPagingApp { ReadsNothing = true }, TimeSpan.Zero);
        session.Dispose();

        var answer = await session.DispatchAsync(Click(click))
            .WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.Empty(answer);
        Assert.Equal(0, Painted());
    }

    [Fact]
    public async Task A_handlers_page_is_no_longer_waited_for_once_its_session_is_disposed()
    {
        var app = new HeldPagingApp { RowsAreAChilds = true };
        var (session, click) = await Open(app, Patient);
        var dispatch = session.DispatchAsync(Click(click));
        await app.ReadStarted();

        // The read is still held, and the budget is longer than this test may run.
        session.Dispose();
        var answer = await dispatch.WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);
        app.Release();

        Assert.Empty(answer);
        Assert.Equal(0, Painted());
    }

    [Fact]
    public async Task The_browser_is_told_the_reads_are_settled_though_the_session_was_disposed_under_them()
    {
        var app = new HeldPagingApp { RowsAreAChilds = true };
        var (session, click) = await Open(app, Brief);
        await session.DispatchAsync(Click(click));
        var settled = session.ReadsSettledAsync();
        var paintedAtTheBudget = Painted();
        session.Dispose();

        app.Release();
        await settled.WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.Equal(paintedAtTheBudget, Painted());
    }

    [Fact]
    public async Task A_session_disposed_twice_is_disposed_once()
    {
        var (session, _) = await Open(new HeldPagingApp(), TimeSpan.Zero);
        session.Dispose();

        var again = Record.Exception(session.Dispose);

        Assert.Null(again);
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
