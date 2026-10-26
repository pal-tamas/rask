using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
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
        await page.Gates.Reached("countries", 1);
        page.Gates.Open("countries", 1);
        await page.Gates.Reached("countries", 2);
        await page.Click("nothing");
        page.Gates.Open("countries", 2);
        await page.Gates.Done("countries");

        var read = page.Gates.Read("countries");
        Assert.Null(read.Failure);
        await page.Shows("countries row");
        Assert.Same(read.First, read.Again);
        await WaitFor.True(() => read.First!.Disposed, "the hook's scope to be disposed once the hook is done");
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task Two_navigations_in_a_row_each_keep_their_own_scope_while_their_hooks_run(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.Navigate("/hook-scope/countries");
        await page.Gates.Reached("countries", 1);
        await page.Navigate("/hook-scope/refunds");
        await page.Gates.Reached("refunds", 1);
        page.Gates.Open("countries", 1);
        page.Gates.Open("countries", 2);
        await page.Gates.Done("countries");
        page.Gates.Open("refunds", 1);
        page.Gates.Open("refunds", 2);
        await page.Gates.Done("refunds");

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
        await page.Gates.Reached("countries", 1);
        var held = page.Gates.Read("countries").First!;
        var heldWhileOnThePage = held.Disposed;
        await page.Navigate("/hook-scope");

        Assert.False(heldWhileOnThePage);
        await WaitFor.True(() => held.Disposed, "the scope of a hook whose component has gone to be disposed");
    }

    // A report's hook takes its unit of work, then hands the slow read to a task of its own and returns: the page
    // never waits for that read, and the read lands over the socket once there is one.
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_first_request_whose_hook_detaches_its_read_is_answered_at_once_and_the_rows_follow(LiveTransportKind transport)
    {
        var host = Host();
        using var answered = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var gates = host.Server.Services.GetRequiredService<HookScopeGates>();

        var html = await host.Http.GetStringAsync("/hook-scope/report", answered.Token);
        await using var page = await AttachAsync(host, transport, html);
        gates.Open("report", 1);

        await page.Shows("report row");
        await WaitFor.True(() => gates.Read("report").First!.Disposed, "the report's scope to go once its read is back");
    }

    [Fact]
    public async Task Closing_a_report_mid_read_disposes_its_scope_with_the_session()
    {
        var host = Host(o => o.SessionGracePeriod = TimeSpan.FromMilliseconds(200));
        var gates = host.Server.Services.GetRequiredService<HookScopeGates>();
        var html = await host.Http.GetStringAsync("/hook-scope/report", TestContext.Current.CancellationToken);
        await using var page = await AttachAsync(host, LiveTransportKind.WebSocket, html);
        await gates.Reached("report", 1);
        var db = gates.Read("report").First!;

        await page.Leave();

        await WaitFor.True(() => db.DisposeStarted, "the report's scope to be disposed with the session it was read for");
        gates.Open("report", 1);
        await WaitFor.True(() => db.Disposed, "the dispose to finish once the read it waited for is back");
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_navigation_whose_hook_detaches_its_read_is_sent_and_the_rows_follow(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.Navigate("/hook-scope/report");
        await page.Gates.Reached("report", 1);
        await page.Click("nothing");
        page.Gates.Open("report", 1);

        await page.Shows("report row");
    }

    [Fact]
    public async Task The_session_ending_disposes_the_scope_a_hook_still_holds()
    {
        await using var page = await OpenAsync(LiveTransportKind.WebSocket);

        await page.Navigate("/hook-scope/countries");
        await page.Gates.Reached("countries", 1);
        var held = page.Gates.Read("countries").First!;
        await page.Session.DisposeAsync();

        await WaitFor.True(() => held.Disposed, "the scope to be disposed with the session");
        page.Gates.Open("countries", 1);
        page.Gates.Open("countries", 2);
    }

    private static RaskTestHost Host(Action<RaskServerOptions>? configure = null) =>
        RaskTestHost.Create<HookScopeApp>(
            services =>
            {
                services.AddSingleton<HookScopeGates>();
                services.AddScoped<HookScopeDb>();
            },
            configureServer: configure);

    private static async Task<OpenPage> OpenAsync(LiveTransportKind transport)
    {
        var host = Host();
        var html = await host.Http.GetStringAsync("/hook-scope", TestContext.Current.CancellationToken);
        return await AttachAsync(host, transport, html);
    }

    private static async Task<OpenPage> AttachAsync(RaskTestHost host, LiveTransportKind transport, string html)
    {
        var session = host.Store.Peek(MarkupAssert.SessionId(html))!;
        var settled = session.AttachesSettled;
        var connection = await LiveTestConnection.OpenAsync(host, transport, session.Id);
        await WaitFor.True(() => session.AttachesSettled > settled, "the attach has adopted its connection's user");
        await session.LastHandlerTask;
        return new OpenPage(host, connection, session, html);
    }

    private sealed class OpenPage(RaskTestHost host, ILiveTestConnection connection, LiveSession session, string html)
        : IAsyncDisposable
    {
        private bool _left;

        public LiveSession Session => session;

        public HookScopeGates Gates => host.Server.Services.GetRequiredService<HookScopeGates>();

        public Task Navigate(string path) => connection.SendJsonAsync(new { type = "navigate", path, query = "" });

        public async Task Click(string button)
        {
            var id = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" + button).Groups[1].Value;
            await connection.SendJsonAsync(new { id });
            await connection.SettledAsync();
        }

        // The tab closes: the session waits out its grace for a reconnect, then ends.
        public async Task Leave()
        {
            _left = true;
            await connection.DisposeAsync();
        }

        public Task Shows(string text) =>
            connection.ReceiveUntilAsync(frame => frame.Contains(text, StringComparison.Ordinal), $"a frame showing {text}");

        public async ValueTask DisposeAsync()
        {
            Gates.OpenAll();
            if (!_left)
            {
                await connection.DisposeAsync();
            }

            await host.StopAsync();
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
