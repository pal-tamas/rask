using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Demos.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// Each test drives the component through its parent wrapper (LiveHost), so the
// real framework — render-walk, lifecycle dispatch, prop diff, unmount — is what
// fires the hooks. The shared FakeJsRuntime stands in for IJSRuntime; the price
// feed is fully synthetic (see LiveTicker.PollOnceAsync) so no HttpClient mock
// is needed.
public sealed class LiveTickerTests
{
    [Fact]
    public async Task OnMountAsync_PopulatesHistoryFromSyntheticFeed()
    {
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(js, symbol, interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => js.GetCalls("sessionStorage.setItem").Count >= 1,
            TimeSpan.FromSeconds(2));

        Assert.Contains(host.Log.Snapshot(), l => l.Contains("OnMount:"));
        Assert.NotEmpty(js.GetCalls("sessionStorage.setItem"));
    }

    [Fact]
    public async Task OnMountAsync_HydratesFromSessionStorage_WhenPresent()
    {
        var stored = JsonSerializer.Serialize(new[]
        {
            new PricePoint(DateTimeOffset.UtcNow.AddSeconds(-3), 64500m),
            new PricePoint(DateTimeOffset.UtcNow.AddSeconds(-2), 64750m)
        });
        var js = new FakeJsRuntime();
        js.SetResponse("sessionStorage.getItem", stored);

        var symbol = new Box<string>("BTC");
        var host = BuildHost(js, symbol, interval: 1000);

        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.Log.Contains("loaded 2 persisted points"), TimeSpan.FromSeconds(2));

        Assert.Contains(host.Log.Snapshot(), l => l.Contains("loaded 2 persisted points"));
    }

    [Fact]
    public async Task OnRenderedAsync_InvokesRaskLiveTickerDraw()
    {
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(js, symbol, interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(() => js.GetCalls("Rask.LiveTicker.draw").Count > 0, TimeSpan.FromSeconds(2));

        Assert.NotEmpty(js.GetCalls("Rask.LiveTicker.draw"));
    }

    [Fact]
    public async Task OnPropsChanged_LogsSymbolSwitch()
    {
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(js, symbol, interval: 30);

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
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(js, symbol, interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.Log.Contains("OnMountAsync"), TimeSpan.FromSeconds(2));

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.Log.Contains("OnUnmountAsync: flushed"), TimeSpan.FromSeconds(2));

        Assert.Contains(host.Log.Snapshot(), l => l.Contains("OnUnmount: stopping"));
        Assert.Contains(host.Log.Snapshot(), l => l.Contains("OnUnmountAsync: flushed"));
    }

    [Fact]
    public async Task PollLoop_StopsAfterUnmount()
    {
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(js, symbol, interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(() => js.GetCalls("sessionStorage.setItem").Count >= 1, TimeSpan.FromSeconds(2));

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.Log.Contains("OnUnmountAsync: flushed"), TimeSpan.FromSeconds(2));

        var persistedAfterUnmount = js.GetCalls("sessionStorage.setItem").Count;
        await Task.Delay(200);
        Assert.Equal(persistedAfterUnmount, js.GetCalls("sessionStorage.setItem").Count);
    }

    [Fact]
    public void PricePointArray_RoundTripsViaContext()
    {
        var points = new[]
        {
            new PricePoint(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), 65000.50m),
            new PricePoint(DateTimeOffset.FromUnixTimeSeconds(1_700_000_010), 65010.75m)
        };
        var json = JsonSerializer.Serialize(points, LiveTickerJsonContext.Default.PricePointArray);
        var roundTripped = JsonSerializer.Deserialize(json, LiveTickerJsonContext.Default.PricePointArray);
        Assert.NotNull(roundTripped);
        Assert.Equal(2, roundTripped!.Length);
        Assert.Equal(65000.50m, roundTripped[0].PriceUsd);
    }

    // Regression: IJSRuntime serializes public property names with the
    // camelCase JsonNamingPolicy, so a C# PricePoint(Timestamp, PriceUsd)
    // lands in JS as { timestamp, priceUsd } — NOT PascalCase. The Chart.js
    // bridge in LiveTicker.js was reading p.Timestamp / p.PriceUsd, which
    // surfaced on the chart as "Invalid Date" and zero-valued bars. This
    // pins the actual call sites (not comments) to the wire shape.
    [Fact]
    public void LiveTickerJs_ReadsCamelCasedPropertyNames()
    {
        var path = Path.Combine(LocateRepoRoot(),
            "Rask.Example.Shared", "Demos", "LiveTicker.js");
        var source = File.ReadAllText(path);

        // Strip line and block comments so the test isn't fooled by the
        // explainer comment that has to mention the PascalCase pitfall.
        var lineCommentStripped = System.Text.RegularExpressions.Regex.Replace(
            source, "//[^\n]*", "");
        var code = System.Text.RegularExpressions.Regex.Replace(
            lineCommentStripped, "/\\*.*?\\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);

        Assert.Matches(@"\.map\(\s*p\s*=>\s*formatTime\(\s*p\.timestamp\s*\)\s*\)", code);
        Assert.Matches(@"\.map\(\s*p\s*=>\s*Number\(\s*p\.priceUsd\s*\)\s*\)", code);
        Assert.DoesNotContain("p.Timestamp", code);
        Assert.DoesNotContain("p.PriceUsd", code);
    }

    // sessionStorage.getItem throwing (private mode / blocked storage) is swallowed
    // by LoadFromStorageAsync — the component starts with an empty history and the
    // poll loop still runs.
    [Fact]
    public async Task OnMountAsync_StorageLoadFailure_StartsFreshAndPolls()
    {
        var js = new FakeJsRuntime();
        js.SetException("sessionStorage.getItem", new InvalidOperationException("storage blocked"));
        var symbol = new Box<string>("BTC");
        var host = BuildHost(js, symbol, interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => js.GetCalls("sessionStorage.setItem").Count >= 1,
            TimeSpan.FromSeconds(2),
            "feed never populated after a storage-load failure");

        Assert.Contains(host.Log.Snapshot(), l => l.Contains("loaded 0 persisted points"));
        Assert.NotEmpty(js.GetCalls("Rask.LiveTicker.draw"));
    }

    // sessionStorage.setItem throwing (quota exceeded) is swallowed by PersistAsync —
    // the poll loop keeps ticking across renders and never surfaces a _error alert
    // (a persist failure is best-effort, not a feed error).
    [Fact]
    public async Task PollLoop_PersistFailure_ContinuesWithoutError()
    {
        var js = new FakeJsRuntime();
        js.SetException("sessionStorage.setItem", new InvalidOperationException("quota exceeded"));
        var symbol = new Box<string>("BTC");
        var host = BuildHost(js, symbol, interval: 30);

        host.RenderAsLiveRoot();
        // Each poll cycle attempts a persist; FakeJsRuntime records the call before
        // throwing, so ≥2 attempts proves the loop kept ticking past the failure.
        await WaitFor.True(
            () => js.GetCalls("sessionStorage.setItem").Count >= 2,
            TimeSpan.FromSeconds(2),
            "poll loop did not survive repeated persist failures");

        // A swallowed persist failure must not light up the #ticker-error alert.
        Assert.DoesNotContain("Feed error", host.RenderAsLiveRoot());
    }

    // History is bounded at HistoryCapacity (60). Seed 60 persisted points, then let
    // one live tick push to 61 and assert the oldest rolled off so the persisted
    // entry stays capped at 60.
    [Fact]
    public async Task PollLoop_HistoryRollover_StaysCappedAt60()
    {
        var seededFirst = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000);
        var seeded = Enumerable.Range(0, 60)
            .Select(i => new PricePoint(seededFirst.AddSeconds(i), 70_000m + i))
            .ToArray();
        var js = new FakeJsRuntime();
        js.SetResponse("sessionStorage.getItem", JsonSerializer.Serialize(seeded));
        var symbol = new Box<string>("BTC");
        var host = BuildHost(js, symbol, interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => js.GetCalls("sessionStorage.setItem").Count >= 1,
            TimeSpan.FromSeconds(2),
            "no tick persisted after seeding a full history");

        var lastPersisted = js.GetCalls("sessionStorage.setItem")[^1]!;
        var json = (string)lastPersisted[1]!;
        var persisted = JsonSerializer.Deserialize(json, LiveTickerJsonContext.Default.PricePointArray)!;

        Assert.Equal(60, persisted.Length);
        Assert.DoesNotContain(persisted, p => p.Timestamp == seededFirst);
    }

    // PollOnceAsync captures the symbol before its simulated latency and drops the
    // result if the symbol changed mid-flight, so a stale asset's price never
    // contaminates the new symbol's chart. The switch also wakes the loop, so the
    // first thing actually persisted is the NEW symbol's tick — never the stale one.
    [Fact]
    public async Task PollOnce_SymbolChangesMidFlight_DropsStaleResult()
    {
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        // Large interval ⇒ steady-state polling is gated; the symbol switch is what
        // drives the next poll (via the wake). The first BTC poll parks in the 50 ms
        // simulated-latency Task.Delay while we flip the symbol underneath it.
        var host = BuildHost(js, symbol, interval: 5000);

        host.RenderAsLiveRoot();
        // The flip lands within microseconds — well inside the 50 ms in-flight window,
        // which is the determinism margin for this race.
        symbol.Value = "ETH";
        host.RenderAsLiveRoot();

        // The woken loop polls ETH and persists it; the dropped BTC tick (≈70 000)
        // never reaches storage.
        await WaitFor.True(
            () => js.GetCalls("sessionStorage.setItem").Count >= 1,
            TimeSpan.FromSeconds(2),
            "the symbol switch did not wake the poll loop");

        var firstPersisted = js.GetCalls("sessionStorage.setItem")[0]!;
        var persisted = JsonSerializer.Deserialize(
            (string)firstPersisted[1]!, LiveTickerJsonContext.Default.PricePointArray)!;

        // Everything written is ETH-magnitude (seed ≈2 500); a stale BTC-magnitude
        // price would prove the mid-flight result leaked through.
        Assert.NotEmpty(persisted);
        Assert.All(persisted, p => Assert.True(
            p.PriceUsd < 10_000m,
            $"a stale BTC-magnitude price ({p.PriceUsd}) leaked into ETH's history"));
    }

    // A Symbol switch cancels the inter-tick delay (_wake), so the new asset is polled
    // immediately instead of waiting out the full interval — the responsiveness fix.
    [Fact]
    public async Task SymbolSwitch_TriggersImmediatePoll()
    {
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        // Large interval: without the wake the next poll wouldn't run for 5 s. The
        // assertion is that switching symbols polls the new asset well inside that.
        var host = BuildHost(js, symbol, interval: 5000);

        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => js.GetCalls("sessionStorage.setItem").Count >= 1,
            TimeSpan.FromSeconds(2),
            "first poll never ran");
        var beforeSwitch = js.GetCalls("sessionStorage.setItem").Count;

        symbol.Value = "ETH";
        host.RenderAsLiveRoot();

        // A fresh persist lands far sooner than the 5 s interval would allow.
        await WaitFor.True(
            () => js.GetCalls("sessionStorage.setItem").Count > beforeSwitch,
            TimeSpan.FromSeconds(2),
            "symbol switch did not wake the poll loop for an immediate tick");
    }

    // OnRenderedAsync is version-gated: a re-render that didn't change the rolling
    // buffer must not re-marshal the array to Chart.js (no redundant draw). A real
    // buffer change still draws.
    [Fact]
    public async Task OnRenderedAsync_SkipsDraw_WhenHistoryUnchanged()
    {
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        // Large interval ⇒ one poll fires early, then the loop parks, so the buffer
        // is stable while we probe the redraw gate.
        var host = BuildHost(js, symbol, interval: 5000);

        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => js.GetCalls("sessionStorage.setItem").Count >= 1,
            TimeSpan.FromSeconds(2),
            "first poll never ran");
        // Let the first tick settle (the loop is now parked for 5 s), then render once
        // to flush the pending tick's data into a draw so _lastDrawnVersion == _version.
        await Task.Delay(100);
        host.RenderAsLiveRoot();
        await Task.Delay(50);

        var drawsAfterFirstTick = js.GetCalls("Rask.LiveTicker.draw").Count;
        Assert.True(drawsAfterFirstTick > 0);

        // Re-renders with no data change must NOT add draws.
        host.RenderAsLiveRoot();
        host.RenderAsLiveRoot();
        await Task.Delay(50);
        Assert.Equal(drawsAfterFirstTick, js.GetCalls("Rask.LiveTicker.draw").Count);

        // A real buffer change (symbol switch clears + reloads history) draws again.
        symbol.Value = "ETH";
        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => js.GetCalls("Rask.LiveTicker.draw").Count > drawsAfterFirstTick,
            TimeSpan.FromSeconds(2),
            "a buffer change did not trigger a redraw");
    }

    // A price source that throws is caught in PollOnceAsync and surfaced through the
    // #ticker-error alert (the production "swap in a real HTTP feed" failure path).
    [Fact]
    public async Task PollLoop_FeedFailure_SurfacesErrorAlert()
    {
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(
            js, symbol, interval: 30,
            priceSource: _ => throw new InvalidOperationException("feed offline"));

        host.RenderAsLiveRoot();
        await WaitFor.True(
            () => host.RenderAsLiveRoot().Contains("Feed error: feed offline"),
            TimeSpan.FromSeconds(2),
            "a throwing price source never surfaced the #ticker-error alert");

        var html = host.RenderAsLiveRoot();
        Assert.Contains("ticker-error", html);
        Assert.Contains("Feed error: feed offline", html);
    }

    // Two LiveTicker instances on one page contribute the same Chart.js <script> in
    // their Head; the framework dedupes head assets by rendered HTML, so the shell
    // emits exactly one chart.umd.js tag.
    [Fact]
    public void MultipleInstances_ShareSingleChartScript()
    {
        var js = new FakeJsRuntime();
        var log = new LifecycleLog();
        var services = LiveHost.Services((typeof(Microsoft.JSInterop.IJSRuntime), js));

        var html = new TwoTickerRoot(log.Add).RenderAsLiveRoot(services);

        Assert.Single(Regex.Matches(html, "chart.umd.js"));
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
                    LiveTicker(Symbol: "BTC", Interval: 5000, Log: log),
                    LiveTicker(Symbol: "ETH", Interval: 5000, Log: log)
                ]
            ]
        ];
    }

    private static LiveHost BuildHost(
        FakeJsRuntime js, Box<string> symbol, int interval, Func<string, decimal>? priceSource = null)
    {
        LiveHost? host = null;
        host = new LiveHost(
            () => LiveTicker(
                Symbol: symbol.Value, Interval: interval, Log: host!.Log.Add, PriceSource: priceSource),
            LiveHost.Services((typeof(Microsoft.JSInterop.IJSRuntime), js)));
        return host;
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.sln"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.sln walking up from {AppContext.BaseDirectory}");
    }

    private sealed class Box<T>(T initial)
    {
        public T Value { get; set; } = initial;
    }
}
