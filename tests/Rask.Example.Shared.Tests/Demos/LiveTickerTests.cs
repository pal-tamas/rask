using System.Globalization;
using System.Text.RegularExpressions;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// Each test drives the component through its parent wrapper (LiveHost), so the
// real framework — render-walk, lifecycle dispatch, prop diff, unmount — is what
// fires the hooks. LiveTicker uses no JavaScript: the price feed is fully synthetic
// (see LiveTicker.PollOnceAsync) and the chart is a server-rendered SVG (Sparkline)
// emitted straight from Render(), so the tests observe state through the rendered
// HTML rather than through an IJSRuntime stub.
public sealed class LiveTickerTests
{
    [Fact]
    public async Task OnMountAsync_PopulatesHistoryFromSyntheticFeed()
    {
        var symbol = new Box<string>("BTC");
        var host = BuildHost(symbol, 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => PointCount(host.RenderAsLiveRoot()) >= 1,
            TimeSpan.FromSeconds(2),
            "the synthetic feed never populated the history");

        Assert.Contains(host.Log.Snapshot(), l => l.Contains("OnMount:"));
        var html = host.RenderAsLiveRoot();
        Assert.True(PointCount(html) >= 1);
        // The chart is a server-rendered SVG, drawn straight from the rolling buffer.
        Assert.Contains("<svg", html);
    }

    [Fact]
    public async Task OnPropsChanged_LogsSymbolSwitch()
    {
        var symbol = new Box<string>("BTC");
        var host = BuildHost(symbol, 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.Log.Contains("OnPropsChangedAsync"), TimeSpan.FromSeconds(2));

        symbol.Value = "ETH";
        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.Log.Contains("Symbol BTC → ETH"), TimeSpan.FromSeconds(2));

        Assert.Contains(host.Log.Snapshot(), l => l.Contains("OnPropsChanged: Symbol BTC → ETH"));
        Assert.Contains(host.Log.Snapshot(), l => l.Contains("OnPropsChangedAsync: switched to ETH"));
    }

    [Fact]
    public async Task OnUnmount_FiresOnRemovalFromTree()
    {
        var symbol = new Box<string>("BTC");
        var host = BuildHost(symbol, 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.Log.Contains("OnMountAsync"), TimeSpan.FromSeconds(2));

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.Log.Contains("OnUnmountAsync: flushed"), TimeSpan.FromSeconds(2));

        Assert.Contains(host.Log.Snapshot(), l => l.Contains("OnUnmount: stopping"));
        Assert.Contains(host.Log.Snapshot(), l => l.Contains("OnUnmountAsync: flushed"));
    }

    // The poll loop is linked to the lifetime CancellationToken, so unmount cancels it.
    // The "poll loop cancelled" log is emitted only when the loop actually exits — its
    // appearance after unmount proves the background loop stopped ticking.
    [Fact]
    public async Task PollLoop_StopsAfterUnmount()
    {
        var symbol = new Box<string>("BTC");
        var host = BuildHost(symbol, 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => PointCount(host.RenderAsLiveRoot()) >= 1, TimeSpan.FromSeconds(2),
            "the poll loop never produced a tick");

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.Log.Contains("OnUnmountAsync: flushed"), TimeSpan.FromSeconds(2));
        await WaitFor.True(
            () => host.Log.Contains("poll loop cancelled"), TimeSpan.FromSeconds(2),
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
        var host = BuildHost(symbol, 1, _ => 10_000m + counter++);

        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => PointCount(host.RenderAsLiveRoot()) >= 60, TimeSpan.FromSeconds(8),
            "the history never filled to capacity");

        Assert.Equal(60, PointCount(host.RenderAsLiveRoot()));

        // Prove the loop keeps ticking past capacity (the current/last price climbs with
        // the increasing feed) yet the count stays pinned at 60 — the oldest rolled off.
        var price = PriceFromHtml(host.RenderAsLiveRoot());
        await WaitFor.True(
            () => PriceFromHtml(host.RenderAsLiveRoot()) > price + 5m, TimeSpan.FromSeconds(4),
            "the poll loop stopped ticking after reaching capacity");

        Assert.Equal(60, PointCount(host.RenderAsLiveRoot()));
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
        var host = BuildHost(symbol, 5000);

        host.RenderAsLiveRoot();
        // The flip lands within microseconds — well inside the 50 ms in-flight window,
        // which is the determinism margin for this race.
        symbol.Value = "ETH";
        host.RenderAsLiveRoot();

        await WaitFor.True(
            () => PointCount(host.RenderAsLiveRoot()) >= 1, TimeSpan.FromSeconds(2),
            "the symbol switch did not wake the poll loop");

        // The only price that landed is ETH-magnitude (seed ≈2 500); a stale
        // BTC-magnitude price (≈70 000) would prove the mid-flight result leaked through.
        var price = PriceFromHtml(host.RenderAsLiveRoot());
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
        var host = BuildHost(symbol, 5000);

        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => PointCount(host.RenderAsLiveRoot()) >= 1, TimeSpan.FromSeconds(2),
            "first poll never ran");

        symbol.Value = "ETH";
        host.RenderAsLiveRoot();

        // An ETH-magnitude price lands far sooner than the 5 s interval would allow.
        await WaitFor.True(
            () =>
            {
                var html = host.RenderAsLiveRoot();
                return PointCount(html) >= 1 && PriceFromHtml(html) < 10_000m;
            },
            TimeSpan.FromSeconds(2),
            "symbol switch did not wake the poll loop for an immediate tick");
    }

    // A price source that throws is caught in PollOnceAsync and surfaced through the
    // #ticker-error alert (the production "swap in a real HTTP feed" failure path).
    [Fact]
    public async Task PollLoop_FeedFailure_SurfacesErrorAlert()
    {
        var symbol = new Box<string>("BTC");
        var host = BuildHost(
            symbol, 30,
            _ => throw new InvalidOperationException("feed offline"));

        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => host.RenderAsLiveRoot().Contains("Feed error: feed offline"),
            TimeSpan.FromSeconds(2),
            "a throwing price source never surfaced the #ticker-error alert");

        var html = host.RenderAsLiveRoot();
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

    private static LiveHost BuildHost(
        Box<string> symbol, int interval, Func<string, decimal>? priceSource = null)
    {
        LiveHost? host = null;
        host = new LiveHost(
            () => LiveTicker(
                symbol.Value, interval, host!.Log.Add, priceSource),
            LiveHost.Services());
        return host;
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
    private sealed class TwoTickerRoot(Action<string> log) : Component
    {
        protected override RenderResult Render() =>
        [
            Doctype(),
            Html("en")[
                Head(),
                Body()[
                    LiveTicker("BTC", 5000, log),
                    LiveTicker("ETH", 5000, log)
                ]
            ]
        ];
    }

    private sealed class Box<T>(T initial)
    {
        public T Value { get; set; } = initial;
    }
}
