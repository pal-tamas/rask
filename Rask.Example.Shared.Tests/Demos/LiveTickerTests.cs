using System.Text.Json;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Demos.Components;

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

    private static LiveHost BuildHost(FakeJsRuntime js, Box<string> symbol, int interval)
    {
        LiveHost? host = null;
        host = new LiveHost(
            () => LiveTicker(Symbol: symbol.Value, Interval: interval, Log: host!.Log.Add),
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
