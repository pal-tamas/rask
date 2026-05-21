using System.Text.Json;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Demos.Components;

namespace Rask.Example.Shared.Tests.Demos;

// Each test drives the component through its parent wrapper (LiveHost), so the
// real framework — render-walk, lifecycle dispatch, prop diff, unmount — is what
// fires the hooks. The shared fakes only stand in for IJSRuntime and HttpClient.
public sealed class LiveTickerTests
{
    [Fact]
    public async Task OnMountAsync_PollsCoinGecko_AndPopulatesHistory()
    {
        var (http, fakeHttp) = FakeHttp.WithPrices(("bitcoin", 65000m));
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(http, js, symbol, interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(() => fakeHttp.RequestCount >= 1, TimeSpan.FromSeconds(2));
        await Task.Delay(120); // let a couple of polls run

        Assert.Contains(host.Log.Snapshot(), l => l.Contains("OnMount:"));
        Assert.True(fakeHttp.RequestCount >= 1, "expected at least one CoinGecko poll");
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
        var (http, _) = FakeHttp.WithPrices(("bitcoin", 65000m));
        var js = new FakeJsRuntime();
        js.SetResponse("sessionStorage.getItem", stored);

        var symbol = new Box<string>("BTC");
        var host = BuildHost(http, js, symbol, interval: 1000);

        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.Log.Contains("loaded 2 persisted points"), TimeSpan.FromSeconds(2));

        Assert.Contains(host.Log.Snapshot(), l => l.Contains("loaded 2 persisted points"));
    }

    [Fact]
    public async Task OnRenderedAsync_InvokesRaskLiveTickerDraw()
    {
        var (http, _) = FakeHttp.WithPrices(("bitcoin", 65000m));
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(http, js, symbol, interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(() => js.GetCalls("Rask.LiveTicker.draw").Count > 0, TimeSpan.FromSeconds(2));

        Assert.NotEmpty(js.GetCalls("Rask.LiveTicker.draw"));
    }

    [Fact]
    public async Task OnPropsChanged_LogsSymbolSwitch()
    {
        var (http, _) = FakeHttp.WithPrices(("bitcoin", 65000m), ("ethereum", 3200m));
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(http, js, symbol, interval: 30);

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
        var (http, _) = FakeHttp.WithPrices(("bitcoin", 65000m));
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(http, js, symbol, interval: 30);

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
        var (http, fakeHttp) = FakeHttp.WithPrices(("bitcoin", 65000m));
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(http, js, symbol, interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(() => fakeHttp.RequestCount >= 1, TimeSpan.FromSeconds(2));

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.Log.Contains("OnUnmountAsync: flushed"), TimeSpan.FromSeconds(2));

        var snapshotAfterUnmount = fakeHttp.RequestCount;
        await Task.Delay(200);
        Assert.Equal(snapshotAfterUnmount, fakeHttp.RequestCount);
    }

    [Fact]
    public async Task HttpFailure_ReportsError_AndKeepsLoopAlive()
    {
        var (http, fakeHttp) = FakeHttp.Throwing(new HttpRequestException("network down"));
        var js = new FakeJsRuntime();
        var symbol = new Box<string>("BTC");
        var host = BuildHost(http, js, symbol, interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor.True(() => fakeHttp.RequestCount > 1, TimeSpan.FromSeconds(2));

        Assert.True(fakeHttp.RequestCount > 1, "expected the poll loop to keep retrying after a network error");
    }

    [Fact]
    public void CoinGeckoPriceResponse_DeserializesCleanly()
    {
        const string json = """{"bitcoin":{"usd":65123.45}}""";
        var resp = JsonSerializer.Deserialize(json, LiveTickerJsonContext.Default.CoinGeckoPriceResponse);
        Assert.NotNull(resp);
        Assert.True(resp!.TryGetValue("bitcoin", out var quote));
        Assert.Equal(65123.45m, quote!["usd"]);
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

    private static LiveHost BuildHost(HttpClient http, FakeJsRuntime js, Box<string> symbol, int interval)
    {
        LiveHost? host = null;
        host = new LiveHost(
            () => LiveTicker(Symbol: symbol.Value, Interval: interval, Log: host!.Log.Add),
            LiveHost.Services(http, js));
        return host;
    }

    private sealed class Box<T>(T initial)
    {
        public T Value { get; set; } = initial;
    }
}
