using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Live;

/// <summary>
///     A lifecycle hook is part of the event that started it: what it resolves with <c>Current.Service&lt;T&gt;()</c>
///     — itself or through a helper that takes nothing — stays that event's, alive until the hook is done, though the
///     navigation went out long before and other events have come and gone since.
/// </summary>
public class HookEventScopeTests
{
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_hook_that_outlives_its_navigation_reads_through_its_event_s_scope_until_it_is_done(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.Navigate("/hook-scope/countries");
        await page.Reached("countries", 1);
        page.Open("countries", 1);
        await page.Reached("countries", 2);
        await page.Click("nothing");
        page.Open("countries", 2);
        await page.Done("countries");

        var read = page.Gates.Read("countries");
        Assert.Null(read.Failure);
        await page.Shows("countries row");
        Assert.Same(read.First, read.Again);
        await page.Steps.Run("wait for the hook's scope to go", () =>
            WaitFor.True(() => read.First!.Disposed, "the hook's scope to be disposed once the hook is done"));
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task Two_navigations_in_a_row_each_keep_their_own_scope_while_their_hooks_run(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.Navigate("/hook-scope/countries");
        await page.Reached("countries", 1);
        await page.Navigate("/hook-scope/refunds");
        await page.Reached("refunds", 1);
        page.Open("countries", 1);
        page.Open("countries", 2);
        await page.Done("countries");
        page.Open("refunds", 1);
        page.Open("refunds", 2);
        await page.Done("refunds");

        var countries = page.Gates.Read("countries");
        var refunds = page.Gates.Read("refunds");
        Assert.Null(countries.Failure);
        Assert.Null(refunds.Failure);
        await page.Shows("refunds row");
        Assert.Same(countries.First, countries.Again);
        Assert.Same(refunds.First, refunds.Again);
        Assert.NotSame(countries.First, refunds.First);
    }

    [Fact]
    public async Task A_hook_that_never_ends_lets_its_scope_go_once_its_component_has_left_the_page_and_the_grace_is_over()
    {
        await using var page = await OpenAsync(LiveTransportKind.WebSocket);
        ((Rask.Core.Live.EventServices)page.Session.EventServices).TeardownGrace = TimeSpan.Zero;

        await page.Navigate("/hook-scope/countries");
        await page.Reached("countries", 1);
        var held = page.Gates.Read("countries").First!;
        var heldWhileOnThePage = held.Disposed;
        await page.Navigate("/hook-scope");

        Assert.False(heldWhileOnThePage);
        await page.Steps.Run("wait for the gone component's scope to go", () =>
            WaitFor.True(() => held.Disposed, "the scope of a hook whose component has gone to be disposed"));
    }

    // A report's hook takes its unit of work, then hands the slow read to a task of its own and returns: the page
    // never waits for that read, and the read lands over the socket once there is one.
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_first_request_whose_hook_detaches_its_read_is_answered_at_once_and_the_rows_follow(LiveTransportKind transport)
    {
        var (host, steps) = Host();
        using var answered = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var gates = host.Server.Services.GetRequiredService<HookScopeGates>();

        var html = await steps.Run("GET /hook-scope/report", () => host.Http.GetStringAsync("/hook-scope/report", answered.Token));
        await using var page = await AttachAsync(host, steps, transport, html);
        page.Open("report", 1);

        await page.Shows("report row");
        await steps.Run("wait for the report's scope to go", () =>
            WaitFor.True(() => gates.Read("report").First!.Disposed, "the report's scope to go once its read is back"));
    }

    [Fact]
    public async Task Closing_a_report_mid_read_disposes_its_scope_with_the_session()
    {
        var (host, steps) = Host(o => o.SessionGracePeriod = TimeSpan.FromMilliseconds(200));
        var gates = host.Server.Services.GetRequiredService<HookScopeGates>();
        var html = await steps.Run("GET /hook-scope/report", () =>
            host.Http.GetStringAsync("/hook-scope/report", TestContext.Current.CancellationToken));
        await using var page = await AttachAsync(host, steps, LiveTransportKind.WebSocket, html);
        await page.Reached("report", 1);
        var db = gates.Read("report").First!;

        await page.Leave();

        await steps.Run("wait for the scope's dispose to start", () =>
            WaitFor.True(() => db.DisposeStarted, "the report's scope to be disposed with the session it was read for"));
        page.Open("report", 1);
        await steps.Run("wait for the scope's dispose to finish", () =>
            WaitFor.True(() => db.Disposed, "the dispose to finish once the read it waited for is back"));
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_navigation_whose_hook_detaches_its_read_is_sent_and_the_rows_follow(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.Navigate("/hook-scope/report");
        await page.Reached("report", 1);
        await page.Click("nothing");
        page.Open("report", 1);

        await page.Shows("report row");
    }

    // The work a hook detached can come back while the hook itself is still running — on a loaded machine, between
    // the detach and the hook's return. Its lease was let go only by a return that started after the hook was done,
    // so this one kept the event's scope for the rest of the session: the report's flake on CI, made certain here.
    [Fact]
    public async Task Work_a_hook_detached_that_comes_back_before_the_hook_is_done_still_lets_its_scope_go()
    {
        await using var page = await OpenAsync(LiveTransportKind.WebSocket);

        await page.Navigate("/hook-scope/early");
        await page.Steps.Run("wait for the early hook to finish", () =>
            WaitFor.True(() => page.Gates.Read("early").Done, "the hook to finish after its detached work came back"));

        var read = page.Gates.Read("early");
        Assert.Null(read.Failure);
        await page.Steps.Run("wait for the early scope to go", () =>
            WaitFor.True(() => read.First!.Disposed, TimeSpan.FromSeconds(10), "the scope the detached work read through to go"));
    }

    // A hook's first await can come back on another thread before the walk that started the hook has held its event.
    // An event that ended in that moment took the hook's scope with it, under the running hook: ObjectDisposedException
    // from Current.Service<T>() in the app's OnUpdated, about once in two hundred navigations. Made certain here: the
    // hook keeps its walk until its work has come back or half a second has gone, and the work ends the event itself.
    [Fact]
    public async Task Work_that_comes_back_before_its_walk_has_held_the_event_keeps_the_event_s_scope()
    {
        await using var page = await OpenAsync(LiveTransportKind.WebSocket);
        page.Gates.EndEvent = page.Session.EndEvent;

        await page.Navigate("/hook-scope/before-hold");
        await page.Done("before-hold");

        var read = page.Gates.Read("before-hold");
        Assert.Null(read.Failure);
        Assert.Same(read.First, read.Again);
        await page.Steps.Run("wait for the hook's scope to go", () =>
            WaitFor.True(() => read.First!.Disposed, "the hook's scope to be disposed once the hook is done"));
    }

    [Fact]
    public async Task The_session_ending_disposes_the_scope_a_hook_still_holds()
    {
        await using var page = await OpenAsync(LiveTransportKind.WebSocket);

        await page.Navigate("/hook-scope/countries");
        await page.Reached("countries", 1);
        var held = page.Gates.Read("countries").First!;
        await page.Steps.Run("dispose the session", () => page.Session.DisposeAsync().AsTask());

        await page.Steps.Run("wait for the held scope to go", () =>
            WaitFor.True(() => held.Disposed, "the scope to be disposed with the session"));
        page.Open("countries", 1);
        page.Open("countries", 2);
    }

    // Each host logs Rask's categories at Debug into its own trail, so a failure, however rare, names the step it
    // stood at and carries what the server did up to it — the transport's opening, attach and close lines among it.
    private static (RaskTestHost Host, StepTrail Steps) Host(Action<RaskServerOptions>? configure = null)
    {
        var log = new CapturedLog();
        var host = RaskTestHost.Create<HookScopeApp>(
            services =>
            {
                services.AddLogging(logging =>
                {
                    logging.AddProvider(log);
                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddFilter("Rask", LogLevel.Debug);
                });
                services.AddSingleton<HookScopeGates>();
                services.AddScoped<HookScopeDb>();
            },
            configureServer: configure);
        return (host, new StepTrail(log));
    }

    private static async Task<OpenPage> OpenAsync(LiveTransportKind transport)
    {
        var (host, steps) = Host();
        var html = await steps.Run("GET /hook-scope", () =>
            host.Http.GetStringAsync("/hook-scope", TestContext.Current.CancellationToken));
        return await AttachAsync(host, steps, transport, html);
    }

    private static async Task<OpenPage> AttachAsync(RaskTestHost host, StepTrail steps, LiveTransportKind transport, string html)
    {
        var session = host.Store.Peek(MarkupAssert.SessionId(html))!;
        var settled = session.AttachesSettled;
        var connection = await steps.Run($"open the {transport} connection", () =>
            LiveTestConnection.OpenAsync(host, transport, session.Id));
        await steps.Run("wait for the attach to adopt its connection's user", () =>
            WaitFor.True(() => session.AttachesSettled > settled, "the attach has adopted its connection's user"));
        await steps.Run("wait for the attach's last handler", () => session.LastHandlerTask);
        return new OpenPage(host, steps, connection, session, html);
    }

    private sealed class OpenPage(RaskTestHost host, StepTrail steps, ILiveTestConnection connection, LiveSession session, string html)
        : IAsyncDisposable
    {
        private bool _left;

        public LiveSession Session => session;

        public StepTrail Steps => steps;

        public HookScopeGates Gates => host.Server.Services.GetRequiredService<HookScopeGates>();

        public Task Navigate(string path) =>
            steps.Run($"navigate to {path}", () => connection.SendJsonAsync(new { type = "navigate", path, query = "" }));

        public Task Reached(string page, int gate) =>
            steps.Run($"wait for {page}'s hook to reach gate {gate}", () => Gates.Reached(page, gate));

        public void Open(string page, int gate)
        {
            steps.Mark($"open {page}'s gate {gate}");
            Gates.Open(page, gate);
        }

        public Task Done(string page) => steps.Run($"wait for {page}'s hook to finish", () => Gates.Done(page));

        public Task Click(string button) =>
            steps.Run($"click {button} and settle", async () =>
            {
                var id = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" + button).Groups[1].Value;
                await connection.SendJsonAsync(new { id });
                await connection.SettledAsync();
            });

        // The tab closes: the session waits out its grace for a reconnect, then ends.
        public Task Leave()
        {
            _left = true;
            return steps.Run("close the connection", () => connection.DisposeAsync().AsTask());
        }

        public Task Shows(string text) =>
            steps.Run($"receive a frame showing {text}", () =>
                connection.ReceiveUntilAsync(frame => frame.Contains(text, StringComparison.Ordinal), $"a frame showing {text}"));

        public async ValueTask DisposeAsync()
        {
            Gates.OpenAll();
            if (!_left)
            {
                await steps.Run("close the connection", () => connection.DisposeAsync().AsTask());
            }

            await steps.Run("stop the host", host.StopAsync);
            host.Dispose();
        }
    }
}

/// <summary>What a page's hook reads, held at two points the test opens: before and after it resolves again.</summary>
public sealed class HookScopeGates
{
    private readonly ConcurrentDictionary<(string Page, int Gate), TaskCompletionSource> _gates = new();
    private readonly ConcurrentDictionary<(string Page, int Gate), int> _reached = new();
    private readonly ConcurrentDictionary<string, HookScopeRead> _reads = new();

    /// <summary>Ends the session's event in progress, as another flow of the session may at any moment.</summary>
    public Action EndEvent { get; set; } = static () => { };

    public HookScopeRead Read(string page) => _reads.GetOrAdd(page, static _ => new HookScopeRead());

    public Task Reached(string page, int gate) =>
        WaitFor.True(() => _reached.ContainsKey((page, gate)), $"{page}'s hook to reach gate {gate}");

    public Task Done(string page) => WaitFor.True(() => Read(page).Done, $"{page}'s hook to finish");

    public void Open(string page, int gate) => Gate(page, gate).TrySetResult();

    public void OpenAll()
    {
        foreach (var gate in _gates.Values)
        {
            gate.TrySetResult();
        }
    }

    public Task Pass(string page, int gate)
    {
        _reached[(page, gate)] = 1;
        return Gate(page, gate).Task;
    }

    private TaskCompletionSource Gate(string page, int gate) =>
        _gates.GetOrAdd((page, gate), static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

public sealed class HookScopeRead
{
    public HookScopeDb? First { get; set; }

    public HookScopeDb? Again { get; set; }

    public Exception? Failure { get; set; }

    public bool Done { get; set; }
}

// The app's DbContext: unusable once its scope is disposed, and — as SqlConnection.Close waits for the command in
// flight — disposing it waits for a read it is still serving.
public sealed class HookScopeDb : IDisposable
{
    private readonly ManualResetEventSlim _idle = new(initialState: true);

    public bool Disposed { get; private set; }

    public bool DisposeStarted { get; private set; }

    public string[] Rows(string page)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        return [page + " row"];
    }

    public void BeginRead() => _idle.Reset();

    public void EndRead() => _idle.Set();

    public void Dispose()
    {
        DisposeStarted = true;
        _idle.Wait(TimeSpan.FromSeconds(30));
        Disposed = true;
        _idle.Dispose();
    }
}

// A helper with no constructor, as an app's catalogue is: it reaches the event's services by itself.
public static class HookScopeCatalogue
{
    public static async Task<string[]> Load(string page)
    {
        var gates = Current.Service<HookScopeGates>();
        var read = gates.Read(page);
        try
        {
            read.First = Current.Service<HookScopeDb>();
            await gates.Pass(page, 1);
            read.Again = Current.Service<HookScopeDb>();
            await gates.Pass(page, 2);
            return [.. read.First.Rows(page), .. read.Again.Rows(page)];
        }
        catch (Exception ex)
        {
            read.Failure = ex;
            return [];
        }
        finally
        {
            read.Done = true;
        }
    }
}

public sealed partial class HookScopeApp : Component
{
    protected override Component? HeadAssets => Title["hook scope"];

    protected override Component? Render() =>
        Div[
            Button.OnClick(static () => { })["nothing"],
            Router
        ];
}

[Route("/hook-scope")]
public sealed partial class HookScopeHome : Component
{
    protected override Component? Render() => P["home"];
}

[Route("/hook-scope/countries")]
public sealed partial class HookScopeCountries : Component
{
    private string[] _rows = [];

    protected override async Task OnUpdated() => _rows = await HookScopeCatalogue.Load("countries");

    protected override Component? Render() => P[string.Join(", ", _rows)];
}

[Route("/hook-scope/refunds")]
public sealed partial class HookScopeRefunds : Component
{
    private string[] _rows = [];

    protected override async Task OnUpdated() => _rows = await HookScopeCatalogue.Load("refunds");

    protected override Component? Render() => P[string.Join(", ", _rows)];
}

// A hook whose detached read comes back while the hook is still running: it waits, synchronously, until it has.
[Route("/hook-scope/early")]
public sealed partial class HookScopeEarlyReturn : Component
{
    private string[] _rows = [];

    protected override async Task OnUpdated()
    {
        await Task.Yield();
        var services = Current.Service<IServiceProvider>();
        var gates = services.GetRequiredService<HookScopeGates>();
        var back = new ManualResetEventSlim();

        _ = Run(services, back);
        var cameBack = back.Wait(TimeSpan.FromSeconds(10));

        gates.Read("early").Failure = cameBack ? null : new TimeoutException("the detached read never came back");
        gates.Read("early").Done = true;
    }

    protected override Component? Render() => P[string.Join(", ", _rows)];

    private async Task Run(IServiceProvider services, ManualResetEventSlim back)
    {
        var read = Task.Run(() =>
        {
            var db = services.GetRequiredService<HookScopeDb>();
            services.GetRequiredService<HookScopeGates>().Read("early").First = db;
            return db.Rows("early");
        });

        // The read is back before the await looks at it — what a loaded machine did once in a hundred runs, made
        // certain here — and it still comes back through the hook's context, which is this test's subject. A plain
        // await of a finished task goes on inline instead: nothing ever comes back, so the lease is (rightly) kept
        // until the component leaves the page, and the scope this test waits for never went.
        read.Wait(TestContext.Current.CancellationToken);
        var rows = await read.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);
        _rows = rows;
        back.Set();
    }
}

// A hook whose work comes back through its context while the walk that started it is still on its way to holding the
// event — held there here, until the work has come back or half a second has gone — and ends the event in between.
[Route("/hook-scope/before-hold")]
public sealed partial class HookScopeBeforeHold : Component
{
    protected override Task OnUpdated()
    {
        var gates = Current.Service<HookScopeGates>();
        var cameBack = new ManualResetEventSlim();
        var work = ComeBack(gates, cameBack);
        cameBack.Wait(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        return work;
    }

    protected override Component? Render() => P["before hold"];

    private static async Task ComeBack(HookScopeGates gates, ManualResetEventSlim cameBack)
    {
        var read = gates.Read("before-hold");
        await Task.Yield();
        try
        {
            read.First = Current.Service<HookScopeDb>();
            cameBack.Set();
            gates.EndEvent();
            read.Again = Current.Service<HookScopeDb>();
            read.First.Rows("before-hold");
        }
        catch (ObjectDisposedException ex)
        {
            read.Failure = ex;
        }
        finally
        {
            read.Done = true;
        }
    }
}

// The report as Eszaf writes it: the hook awaits a first read, takes its unit of work, then detaches the slow read
// — a task of its own that reads through the event's services — and returns. Its end repaints the page by itself.
[Route("/hook-scope/report")]
public sealed partial class HookScopeReport : Component
{
    private string[] _rows = [];
    private int _turn;

    protected override async Task OnUpdated()
    {
        await Task.Yield();
        _ = Current.Service<HookScopeDb>();

        // Detached on purpose, as the app does: the page goes out without the rows, and they follow.
        _ = Run(++_turn);
    }

    protected override Component? Render() => P[string.Join(", ", _rows)];

    private async Task Run(int turn)
    {
        var rows = await Read();
        if (turn == _turn)
        {
            _rows = rows;
        }
    }

    private static Task<string[]> Read()
    {
        var services = Current.Service<IServiceProvider>();
        var gates = services.GetRequiredService<HookScopeGates>();
        return Task.Run(async () =>
        {
            var db = services.GetRequiredService<HookScopeDb>();
            gates.Read("report").First = db;
            db.BeginRead();
            try
            {
                await gates.Pass("report", 1);
                return db.Rows("report");
            }
            finally
            {
                db.EndRead();
            }
        });
    }
}
