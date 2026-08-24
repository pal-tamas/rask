using System.Globalization;
using System.Text.RegularExpressions;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Demos;

// Each test drives the component through RaskTest.Render's forwarding root, so the
// real framework — render-walk, lifecycle dispatch, prop diff, unmount — is what
// fires the hooks. LiveTicker uses no JavaScript: the price feed is fully synthetic
// (see LiveTicker.PollOnceAsync) and the chart is a server-rendered SVG (Sparkline)
// emitted straight from Render(), so the tests observe state through the rendered
// HTML rather than through an IJSRuntime stub.
public sealed partial class LiveTickerTests : global::Rask.Core.RaskMarkup
{
    // These are background-async lifecycle waits: OnMountAsync spins up a poll loop
    // whose first tick lands only after a 50 ms Task.Delay, and WaitFor polls the
    // rendered HTML on its own delay. On a cold/starved thread pool (e.g. the nightly
    // unit job runs right after compiling every WASM sample bundle), the thread-pool
    // hill-climber injects worker threads slowly, so those continuations can slip past
    // a tight deadline even though the code is correct. WaitFor.True returns the instant
    // the condition holds, so these generous budgets never slow a healthy run — they
    // only keep a momentarily-starved runner from flaking.
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

    // The cap test must accumulate 60 ticks (≈60 × 65 ms of real delay) before the
    // buffer fills, so it needs a proportionally larger budget than a single-tick wait.
    private static readonly TimeSpan FillToCapacity = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task OnMountAsync_PopulatesHistoryFromSyntheticFeed()
    {
        var symbol = new Box<string>("BTC");
        var (page, log, mounted) = BuildHost(symbol, 30);
        await WaitFor.True(
            () => PointCount(page.Render()) >= 1,
            Settle,
            "the synthetic feed never populated the history");

        Assert.Contains(log.Snapshot(), l => l.Contains("OnMount:"));
        var html = page.Render();
        Assert.True(PointCount(html) >= 1);
        // The chart is a server-rendered SVG, drawn straight from the rolling buffer.
        Assert.Contains("<svg", html);
    }

    [Fact]
    public async Task OnPropsChanged_LogsSymbolSwitch()
    {
        var symbol = new Box<string>("BTC");
        var (page, log, mounted) = BuildHost(symbol, 30);

        // Settle the MOUNT before switching the symbol. This waited on "OnPropsChangedAsync", which cannot
        // have fired yet — nothing has changed a prop at this point — so it spent the full budget and, back
        // when WaitFor swallowed its own timeout, moved on as if it had succeeded. Ten seconds per run,
        // invisible. The sibling unmount test always had this right.
        await WaitFor.True(() => log.Contains("OnMountAsync"), Settle, "the ticker never mounted");

        symbol.Value = "ETH";
        page.Render();
        await WaitFor.True(
            () => log.Contains("Symbol BTC → ETH"), Settle, "the symbol switch was never observed");

        Assert.Contains(log.Snapshot(), l => l.Contains("OnPropsChanged: Symbol BTC → ETH"));
        Assert.Contains(log.Snapshot(), l => l.Contains("OnPropsChangedAsync: switched to ETH"));
    }

    [Fact]
    public async Task OnUnmount_FiresOnRemovalFromTree()
    {
        var symbol = new Box<string>("BTC");
        var (page, log, mounted) = BuildHost(symbol, 30);
        await WaitFor.True(() => log.Contains("OnMountAsync"), Settle);

        mounted.Value = false;
        page.Render();
        await WaitFor.True(() => log.Contains("OnUnmountAsync: flushed"), Settle);

        Assert.Contains(log.Snapshot(), l => l.Contains("OnUnmount: stopping"));
        Assert.Contains(log.Snapshot(), l => l.Contains("OnUnmountAsync: flushed"));
    }

    // The poll loop is linked to the lifetime CancellationToken, so unmount cancels it.
    // The "poll loop cancelled" log is emitted only when the loop actually exits — its
    // appearance after unmount proves the background loop stopped ticking.
    [Fact]
    public async Task PollLoop_StopsAfterUnmount()
    {
        var symbol = new Box<string>("BTC");
        var (page, log, mounted) = BuildHost(symbol, 30);
        await WaitFor.True(
            () => PointCount(page.Render()) >= 1, Settle,
            "the poll loop never produced a tick");

        mounted.Value = false;
        page.Render();
        await WaitFor.True(() => log.Contains("OnUnmountAsync: flushed"), Settle);
        await WaitFor.True(
            () => log.Contains("poll loop cancelled"), Settle,
            "the poll loop did not stop after unmount");
    }

    // History is bounded at HistoryCapacity (60): once full, each new tick rolls the
    // oldest point off (RemoveAt(0)) so the buffer never exceeds 60. A strictly
    // increasing feed drives ticks fast; the count tops out at exactly 60 and stays
    // there while the loop keeps ticking — which is only possible if the oldest is
    // being removed each time.
    [Fact]
    public async Task PollLoop_HistoryStaysCappedAt60()
    {
        var symbol = new Box<string>("BTC");
        var counter = 0;
        var (page, log, mounted) = BuildHost(symbol, 1, _ => 10_000m + counter++);
        await WaitFor.True(
            () => PointCount(page.Render()) >= 60, FillToCapacity,
            "the history never filled to capacity");

        Assert.Equal(60, PointCount(page.Render()));

        // Prove the loop keeps ticking past capacity (the current/last price climbs with
        // the increasing feed) yet the count stays pinned at 60 — the oldest rolled off.
        var price = PriceFromHtml(page.Render());
        await WaitFor.True(
            () => PriceFromHtml(page.Render()) > price + 5m, Settle,
            "the poll loop stopped ticking after reaching capacity");

        Assert.Equal(60, PointCount(page.Render()));
    }

    // PollOnceAsync captures the symbol before its simulated latency and drops the
    // result if the symbol changed mid-flight, so a stale asset's price never
    // contaminates the new symbol's chart. The switch also wakes the loop, so the
    // first price that actually lands is the NEW symbol's tick — never the stale one.
    [Fact]
    public async Task PollOnce_SymbolChangesMidFlight_DropsStaleResult()
    {
        var symbol = new Box<string>("BTC");
        // Large interval ⇒ steady-state polling is gated; the symbol switch is what
        // drives the next poll (via the wake). The first BTC poll parks in the 50 ms
        // simulated-latency Task.Delay while we flip the symbol underneath it.
        var (page, log, mounted) = BuildHost(symbol, 5000);
        // The flip lands within microseconds — well inside the 50 ms in-flight window,
        // which is the determinism margin for this race.
        symbol.Value = "ETH";
        page.Render();

        await WaitFor.True(
            () => PointCount(page.Render()) >= 1, Settle,
            "the symbol switch did not wake the poll loop");

        // The only price that landed is ETH-magnitude (seed ≈2 500); a stale
        // BTC-magnitude price (≈70 000) would prove the mid-flight result leaked through.
        var price = PriceFromHtml(page.Render());
        Assert.True(price < 10_000m, $"a stale BTC-magnitude price ({price}) leaked into ETH's history");
    }

    // A Symbol switch cancels the inter-tick delay (_wake), so the new asset is polled
    // immediately instead of waiting out the full interval — the responsiveness fix.
    [Fact]
    public async Task SymbolSwitch_TriggersImmediatePoll()
    {
        var symbol = new Box<string>("BTC");
        // Large interval: without the wake the next poll wouldn't run for 5 s. The
        // assertion is that switching symbols polls the new asset well inside that.
        var (page, log, mounted) = BuildHost(symbol, 5000);
        await WaitFor.True(
            () => PointCount(page.Render()) >= 1, Settle,
            "first poll never ran");

        symbol.Value = "ETH";
        page.Render();

        // An ETH-magnitude price lands far sooner than the 5 s interval would allow.
        await WaitFor.True(
            () =>
            {
                var html = page.Render();
                return PointCount(html) >= 1 && PriceFromHtml(html) < 10_000m;
            },
            Settle,
            "symbol switch did not wake the poll loop for an immediate tick");
    }

    // A price source that throws is caught in PollOnceAsync and surfaced through the
    // #ticker-error alert (the production "swap in a real HTTP feed" failure path).
    [Fact]
    public async Task PollLoop_FeedFailure_SurfacesErrorAlert()
    {
        var symbol = new Box<string>("BTC");
        var (page, _, _) = BuildHost(
            symbol, 30,
            _ => throw new InvalidOperationException("feed offline"));

        await WaitFor.True(
            () => page.Render().Contains("Feed error: feed offline"),
            Settle,
            "a throwing price source never surfaced the #ticker-error alert");

        var html = page.Render();
        Assert.Contains("ticker-error", html);
        Assert.Contains("Feed error: feed offline", html);
    }

    // Two LiveTicker instances on one page render with no JavaScript: there is no
    // Chart.js <script> and no scoped-JS bundle — the charts are inline SVG.
    [Fact]
    public void MultipleInstances_RenderWithoutAnyJavaScript()
    {
        var log = new LifecycleLog();
        var services = LiveHost.Services();

        var html = new TwoTickerRoot(log.Add).RenderAsLiveRoot(services);

        Assert.DoesNotContain("chart.umd.js", html);
        Assert.DoesNotContain("Rask.LiveTicker", html);
        // Both instances render an SVG chart placeholder/frame.
        Assert.Equal(2, Regex.Matches(html, "ticker-chart-container").Count);
    }

    private static (RenderedComponent Page, LifecycleLog Log, Box<bool> Mounted) BuildHost(
        Box<string> symbol, int interval, Func<string, decimal>? priceSource = null)
    {
        var log = new LifecycleLog();
        var mounted = new Box<bool>(true);
        var page = RaskTest.Render(
            () => mounted.Value
                ? LiveTicker.Symbol(symbol.Value).Interval(interval).Log(log.Add).PriceSource(priceSource)
                : null,
            LiveHost.Services());
        return (page, log, mounted);
    }

    // "<n>/60 pts" in the header reflects _history.Count.
    private static int PointCount(string html)
    {
        var m = Regex.Match(html, @"(\d+)/60 pts");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    // The current price rendered in #ticker-price (e.g. "$70,000.00"); 0 before the first tick.
    private static decimal PriceFromHtml(string html)
    {
        var m = Regex.Match(html, @"id=""ticker-price""[^>]*>\$([0-9,]+\.[0-9]{2})");
        return m.Success
            ? decimal.Parse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture)
            : 0m;
    }

    // Full-shell root hosting two LiveTickers so the head pipeline runs (same
    // RenderAsLiveRoot path App uses). [SkipFactory] keeps the generator from
    // emitting a colliding Generated.TwoTickerRoot() factory.
    [SkipFactory]
    private sealed partial class TwoTickerRoot(Action<string> log) : Component
    {
        protected override Component? Render() =>
        [
            Doctype,
            Html.Lang("en")[
                Head,
                Body[
                    LiveTicker.Symbol("BTC").Interval(5000).Log(log),
                    LiveTicker.Symbol("ETH").Interval(5000).Log(log)
                ]
            ]
        ];
    }

    private sealed class Box<T>(T initial)
    {
        public T Value { get; set; } = initial;
    }
}
