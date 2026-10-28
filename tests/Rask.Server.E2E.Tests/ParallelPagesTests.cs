using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Cqrs;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Many readers on one host at once, each doing what an app's page does: its first rows load as it mounts, a save
///     says so in a toast and the value is read again after it, a job finished elsewhere says so through the page's
///     event handler, a link leads to a list whose rows follow, and a source every page follows ticks throughout.
///     Every answer reaches every page (#1258).
/// </summary>
/// <remarks>
///     <c>RASK_E2E_PARALLEL_PAGES</c>, <c>RASK_E2E_PARALLEL_ROUNDS</c>, <c>RASK_E2E_PARALLEL_STRESS</c> (threads
///     kept busy in the host's process) and <c>RASK_E2E_PARALLEL_CPU_THROTTLE</c> (each page's scripts that many times
///     slower) size a heavier run; unset, it is small enough for every push. The host's session lifecycle is counted
///     and printed, and a tab that comes back to a session the host removed under it fails the run.
/// </remarks>
public sealed class ParallelPagesTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const int ChecksPerRound = 9;

    private static readonly int Pages = Setting("RASK_E2E_PARALLEL_PAGES", 12);
    private static readonly int Rounds = Setting("RASK_E2E_PARALLEL_ROUNDS", 3);
    private static readonly int Stress = Setting("RASK_E2E_PARALLEL_STRESS", 0);
    private static readonly int Throttle = Setting("RASK_E2E_PARALLEL_CPU_THROTTLE", 1);

    // How long an answer may take before it counts as lost: a page whose scripts run N times slower is given N times
    // as long, so what is counted is an answer that never came, not one a throttled page was slow to paint.
    private static readonly float AnswerWithin = 15_000f * Throttle;

    private int _checked;

    [Fact]
    public async Task Every_page_open_beside_the_others_is_shown_what_its_actions_bring_back()
    {
        var board = new DeskBoard();
        var lifecycle = new LifecycleCounts();
        await using var host = await LiveServerHost.StartAsync<BusyDeskPage>(
            blockWebSockets: false,
            services: services => services.AddRaskCqrs().AddSingleton<DeskLedger>().AddSingleton(board)
                .AddLogging(logging => logging.AddProvider(lifecycle).AddFilter("Rask.Live", LogLevel.Debug)));
        using var stress = new BusyThreads(Stress);
        using var ticks = board.TickEvery(TimeSpan.FromMilliseconds(15));
        var lost = new ConcurrentQueue<string>();

        try
        {
            await Task.WhenAll(Enumerable.Range(0, Pages).Select(reader => ReadAsync(host.BaseUrl, reader, lost)));
        }
        finally
        {
            TestContext.Current.TestOutputHelper?.WriteLine(lifecycle.ToString());
            if (Environment.GetEnvironmentVariable("RASK_E2E_LIFECYCLE_LOG") is { Length: > 0 } keep)
            {
                await File.WriteAllLinesAsync(keep, lifecycle.Lines, TestContext.Current.CancellationToken);
            }
        }

        Assert.True(lost.IsEmpty, $"{lost.Count} answers never reached their page:{Environment.NewLine}{string.Join(Environment.NewLine, lost)}"
                                  + $"{Environment.NewLine}{lifecycle}");
        // A tab that came back to a session this host had removed under it is told so, and reloads.
        Assert.True(lifecycle.Refused.IsEmpty, $"a tab came back to a removed session:{Environment.NewLine}"
                                               + $"{string.Join(Environment.NewLine, lifecycle.Refused)}{Environment.NewLine}{lifecycle}");
        // Every check ran: a round cut short is noted as lost above, so this only says nothing was skipped silently.
        Assert.Equal(Pages * Rounds * ChecksPerRound, Volatile.Read(ref _checked));
    }

    private async Task ReadAsync(string baseUrl, int reader, ConcurrentQueue<string> lost)
    {
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1000, Height = 700 } });
        var page = await context.NewPageAsync();
        if (Throttle > 1)
        {
            // A starved browser: every page's scripts run this many times slower, as on a loaded CI runner.
            var cdp = await context.NewCDPSessionAsync(page);
            await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = Throttle });
        }

        // RASK_E2E_FRAMES=<dir>: every frame this reader's page sent and received, kept for a reader that lost one.
        var frames = new ConcurrentQueue<string>();
        var keepFrames = Environment.GetEnvironmentVariable("RASK_E2E_FRAMES");
        if (!string.IsNullOrEmpty(keepFrames))
        {
            page.WebSocket += (_, socket) =>
            {
                frames.Enqueue($"{DateTime.UtcNow:HH:mm:ss.fff} open {socket.Url}");
                socket.FrameSent += (_, f) => frames.Enqueue($"{DateTime.UtcNow:HH:mm:ss.fff} >> {Cut(f.Text)}");
                socket.FrameReceived += (_, f) => frames.Enqueue($"{DateTime.UtcNow:HH:mm:ss.fff} << {Cut(f.Text)}");
                socket.Close += (_, _) => frames.Enqueue($"{DateTime.UtcNow:HH:mm:ss.fff} closed");
            };
            page.Console += (_, m) => frames.Enqueue($"{DateTime.UtcNow:HH:mm:ss.fff} console {m.Type}: {Cut(m.Text)}");
        }

        for (var round = 1; round <= Rounds; round++)
        {
            var at = $"reader {reader}, round {round}";
            try
            {
                await RoundAsync(page, baseUrl, at, lost);
            }
            catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
            {
                // A control that never appeared: the page is not the one the round expects. Noted, and the next round
                // starts from a fresh load.
                lost.Enqueue($"{at}: {ex.Message.Split('\n')[0]} {ex.Message.Split('\n').ElementAtOrDefault(2)}");
            }
        }

        if (!string.IsNullOrEmpty(keepFrames) && lost.Any(l => l.StartsWith($"reader {reader},", StringComparison.Ordinal)))
        {
            await File.WriteAllLinesAsync(Path.Combine(keepFrames, $"reader-{reader}.log"), frames);
        }
    }

    private static string Cut(string? text) => text is null ? "(binary)" : text.Length <= 400 ? text : text[..400] + "…";

    private async Task RoundAsync(IPage page, string baseUrl, string at, ConcurrentQueue<string> lost)
    {
        await page.GotoAsync(baseUrl + "/", new() { Timeout = 60_000 });
        await Seen(page.Locator("#rows"), "row 1, row 2, row 3", $"{at}: first rows", lost);

        for (var save = 1; save <= 2; save++)
        {
            await PressAsync(page, "#save");
            await Seen(page.Locator("#value"), $"value={save}", $"{at}: value read again after save {save}", lost);
            await Seen(page.Locator("[data-ui-toast]").GetByText($"Saved {save}."), null, $"{at}: toast of save {save}", lost);
        }

        await PressAsync(page, "#run");
        await Seen(page.Locator("#job"), "job=1", $"{at}: job heard", lost);
        await Seen(page.Locator("[data-ui-toast]").GetByText("Job 1 done."), null, $"{at}: toast of the job", lost);

        await PressAsync(page, "#to-list");
        await Seen(page.Locator("#list"), "listed 3", $"{at}: a list reached by a link", lost);
        await PressAsync(page, "#to-desk");
        await Seen(page.Locator("#rows"), "row 1, row 2, row 3", $"{at}: back at the desk", lost);
    }

    // Presses a control as a reader does: not while it shows it is still working on the last press (aria-busy), which
    // it refuses by design — a second Save while the first is unanswered is the double submit it exists to stop.
    private static async Task PressAsync(IPage page, string selector)
    {
        var control = page.Locator(selector);
        await Expect(control).Not.ToHaveAttributeAsync("aria-busy", "true", new() { Timeout = AnswerWithin });
        await control.ClickAsync();
    }

    // Waits for what an action brings back, noting it when it never comes rather than ending the reader's walk.
    private async Task Seen(ILocator what, string? text, string described, ConcurrentQueue<string> lost)
    {
        Interlocked.Increment(ref _checked);
        try
        {
            if (text is null)
            {
                await Expect(what).ToHaveCountAsync(1, new() { Timeout = AnswerWithin });
            }
            else
            {
                await Expect(what).ToHaveTextAsync(text, new() { Timeout = AnswerWithin });
            }
        }
        catch (PlaywrightException ex)
        {
            var now = await what.Page.EvaluateAsync<string>(
                "() => ['value', 'job', 'rows', 'list'].map(id => document.getElementById(id)?.textContent ?? '-').join(' | ')");
            var session = await what.Page.EvaluateAsync<string?>("() => document.body.getAttribute('data-rask-root')");
            lost.Enqueue($"{described}: {ex.Message.Split('\n')[0]} (page: {now}; session #{Fingerprint(session)})");
        }
    }

    // What the host's log calls a session: the first four bytes of its id's SHA-256.
    private static string Fingerprint(string? sessionId) => sessionId is null
        ? "-"
        : Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sessionId)).AsSpan(0, 4));

    private static int Setting(string name, int otherwise) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : otherwise;

    /// <summary>
    ///     The host's session lifecycle (<c>Rask.Live</c> at Debug), counted by what each line names — who closed a
    ///     connection and why, how a connection attached, why a session went — and every refused attach kept whole.
    /// </summary>
    private sealed class LifecycleCounts : ILoggerProvider, ILogger
    {
        private static readonly string[] Named = ["ClosedBy", "Reason", "Attach", "CatchUp", "Why"];

        private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, double> _longestWait = new(StringComparer.Ordinal);

        public ConcurrentQueue<string> Refused { get; } = new();

        /// <summary>Every line, timed, for <c>RASK_E2E_LIFECYCLE_LOG</c> to keep.</summary>
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Enqueue($"{DateTime.UtcNow:HH:mm:ss.fff} {formatter(state, exception)}");
            if (eventId.Id is > 1100 and < 1200 && state is IReadOnlyList<KeyValuePair<string, object?>> fields)
            {
                var kind = string.Join(" / ", fields.Where(static f => Named.Contains(f.Key)).Select(static f => f.Value));
                _counts.AddOrUpdate($"{eventId.Id} {kind}", 1, static (_, n) => n + 1);
                if (fields.FirstOrDefault(static f => f.Key == "Waited").Value is double waited)
                {
                    _longestWait.AddOrUpdate(kind, waited, (_, longest) => Math.Max(longest, waited));
                }
            }
            else if (formatter(state, exception) is var line && line.Contains("refused to attach", StringComparison.Ordinal))
            {
                Refused.Enqueue(line);
            }
        }

        public override string ToString() =>
            "lifecycle:" + string.Concat(_counts.OrderBy(static c => c.Key, StringComparer.Ordinal)
                .Select(static c => $"{Environment.NewLine}  {c.Value,5} × {c.Key}"))
            + string.Concat(_longestWait.Select(static w => $"{Environment.NewLine}  longest wait {w.Value:0.000} s: {w.Key}"));

        public void Dispose()
        {
        }
    }

    /// <summary>Threads that keep the host's cores busy for as long as the journey runs: a loaded machine.</summary>
    private sealed class BusyThreads : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread[] _threads;

        public BusyThreads(int count)
        {
            _threads = [.. Enumerable.Range(0, count).Select(_ => new Thread(Spin) { IsBackground = true })];
            foreach (var thread in _threads)
            {
                thread.Start();
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            foreach (var thread in _threads)
            {
                thread.Join();
            }

            _stop.Dispose();
        }

        private void Spin()
        {
            var x = 0UL;
            while (!_stop.IsCancellationRequested)
            {
                x = (x * 6364136223846793005UL) + 1442695040888963407UL;
            }

            GC.KeepAlive(x);
        }
    }
}

/// <summary>What the desks have saved: the value a page reads again after its save.</summary>
public sealed class DeskLedger
{
    private readonly ConcurrentDictionary<string, int> _saved = new(StringComparer.Ordinal);

    public int Read(string desk) => _saved.GetValueOrDefault(desk);

    public void Add(string desk) => _saved.AddOrUpdate(desk, 1, static (_, n) => n + 1);
}

/// <summary>What every page follows in its frame, ticked from another thread: an app's pump board.</summary>
public sealed class DeskBoard
{
    private int _ticks;

    public event Action? Changed;

    public int Ticks => Volatile.Read(ref _ticks);

    public IDisposable TickEvery(TimeSpan every) => new Timer(_ =>
    {
        Interlocked.Increment(ref _ticks);
        Changed?.Invoke();
    }, null, every, every);
}

/// <summary>The frame's part that follows the board, as an app's notification centre does.</summary>
public sealed partial class DeskNotices(DeskBoard board) : Component
{
    protected override Task OnMount()
    {
        board.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        board.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() => Span.Class("ticks")[(board.Ticks % 7).ToString(CultureInfo.InvariantCulture)];
}

/// <summary>A list whose rows are read as it mounts, reached by a link: the page goes at once, its rows follow.</summary>
public sealed partial class DeskList : Component
{
    private int? _rows;

    protected override async Task OnMount()
    {
        await Task.Delay(Random.Shared.Next(20, 300), CancellationToken);
        _rows = 3;
    }

    protected override Component? Render() => P.Id("list")[_rows is { } rows ? $"listed {rows}" : "reading"];
}

public sealed record DeskJobDone(string Desk, int Round) : IEvent;

/// <summary>A page as an app's is: rows loaded as it mounts, a save with a toast, and a job that reports back.</summary>
public sealed partial class BusyDeskPage(IDispatcher dispatcher, DeskLedger ledger, RouteState route)
    : Component, IEventHandler<DeskJobDone>
{
    private readonly string _desk = Guid.NewGuid().ToString("N");
    private string[]? _rows;
    private int _runs;
    private int _heard;
    private int _value;

    protected override Component? HeadAssets => Markup.Title["busy desk"];

    protected override string? HtmlLang => "en";

    public Task Handle(DeskJobDone e)
    {
        if (string.Equals(e.Desk, _desk, StringComparison.Ordinal))
        {
            _heard = e.Round;
            Toast.Success($"Job {e.Round} done.").UntilDismissed();
        }

        return Task.CompletedTask;
    }

    protected override async Task OnMount()
    {
        route.Changed += StateHasChanged;
        await Task.Delay(Random.Shared.Next(5, 60));
        _rows = ["row 1", "row 2", "row 3"];
    }

    protected override Task OnUnmount()
    {
        route.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() => route.Path == "/list"
        ? Div[DeskList.Key("list"), NavLink.Href("/").Id("to-desk")["desk"], DeskNotices, Ui.Toast]
        : Div[
            NavLink.Href("/list").Id("to-list")["list"],
            P.Id("rows")[_rows is null ? "loading" : string.Join(", ", _rows)],
            P.Id("value")[$"value={_value}"],
            P.Id("job")[$"job={_heard}"],
            Button.Id("save").OnClick(Save)["Save"],
            Button.Id("run").OnClick(Run)["Run"],
            DeskNotices,
            Ui.Toast
        ];

    private async Task Save()
    {
        await Task.Delay(Random.Shared.Next(5, 40));
        ledger.Add(_desk);
        Toast.Success($"Saved {ledger.Read(_desk)}.").UntilDismissed();
        _ = ReadAgainAsync();
    }

    // The figure read again as a query whose key the save moved is: after the save's page has gone out, on the
    // thread pool, and drawn by a render of its own.
    private async Task ReadAgainAsync()
    {
        await Task.Delay(Random.Shared.Next(100, 300)).ConfigureAwait(false);
        _value = ledger.Read(_desk);
        StateHasChanged();
    }

    private void Run()
    {
        var round = ++_runs;
        var desk = _desk;
        _ = Task.Run(async () =>
        {
            await Task.Delay(Random.Shared.Next(20, 200));
            await dispatcher.Publish(new DeskJobDone(desk, round));
        });
    }
}
