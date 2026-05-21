using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Live;
using Rask.Example.Shared.Demos;
using static Rask.Example.Shared.Demos.Components;

namespace Rask.Example.Shared.Tests.Demos;

// Each test drives the component through its parent wrapper (TickerHost), so the
// real framework — render-walk, lifecycle dispatch, prop diff, unmount — is what
// fires the hooks. The fakes only stand in for IJSRuntime and HttpClient.
public sealed class LiveTickerTests
{
    [Fact]
    public async Task OnMountAsync_PollsCoinCap_AndPopulatesHistory()
    {
        var (http, fakeHttp) = FakeHttp.WithPrices(("bitcoin", 65000m));
        var js = new FakeJsRuntime();
        var (host, _) = BuildHost(http, js, "BTC", interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor(() => fakeHttp.RequestCount >= 1, TimeSpan.FromSeconds(2));
        await Task.Delay(120); // let a couple of polls run

        Assert.Contains(host.Log, l => l.Contains("OnMount:"));
        Assert.True(fakeHttp.RequestCount >= 1, "expected at least one CoinCap poll");
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

        var (host, _) = BuildHost(http, js, "BTC", interval: 1000);

        host.RenderAsLiveRoot();
        await WaitFor(() => host.LastLogContains("OnMountAsync: loaded 2 persisted points"), TimeSpan.FromSeconds(2));

        Assert.Contains(host.Log, l => l.Contains("loaded 2 persisted points"));
    }

    [Fact]
    public async Task OnRenderedAsync_InvokesRaskLiveTickerDraw()
    {
        var (http, _) = FakeHttp.WithPrices(("bitcoin", 65000m));
        var js = new FakeJsRuntime();
        var (host, _) = BuildHost(http, js, "BTC", interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor(() => js.GetCalls("Rask.LiveTicker.draw").Count > 0, TimeSpan.FromSeconds(2));

        Assert.NotEmpty(js.GetCalls("Rask.LiveTicker.draw"));
    }

    [Fact]
    public async Task OnPropsChanged_LogsSymbolSwitch()
    {
        var (http, _) = FakeHttp.WithPrices(("bitcoin", 65000m), ("ethereum", 3200m));
        var js = new FakeJsRuntime();
        var (host, _) = BuildHost(http, js, "BTC", interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor(() => host.LastLogContains("OnPropsChangedAsync"), TimeSpan.FromSeconds(2));

        host.Symbol = "ETH";
        host.RenderAsLiveRoot();
        await WaitFor(() => host.LastLogContains("Symbol BTC → ETH"), TimeSpan.FromSeconds(2));

        Assert.Contains(host.Log, l => l.Contains("OnPropsChanged: Symbol BTC → ETH"));
        Assert.Contains(host.Log, l => l.Contains("OnPropsChangedAsync: switched to ETH"));
    }

    [Fact]
    public async Task OnUnmount_FiresOnRemovalFromTree()
    {
        var (http, _) = FakeHttp.WithPrices(("bitcoin", 65000m));
        var js = new FakeJsRuntime();
        var (host, _) = BuildHost(http, js, "BTC", interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor(() => host.LastLogContains("OnMountAsync"), TimeSpan.FromSeconds(2));

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor(() => host.LastLogContains("OnUnmountAsync: flushed"), TimeSpan.FromSeconds(2));

        Assert.Contains(host.Log, l => l.Contains("OnUnmount: stopping"));
        Assert.Contains(host.Log, l => l.Contains("OnUnmountAsync: flushed"));
    }

    [Fact]
    public async Task PollLoop_StopsAfterUnmount()
    {
        var (http, fakeHttp) = FakeHttp.WithPrices(("bitcoin", 65000m));
        var js = new FakeJsRuntime();
        var (host, _) = BuildHost(http, js, "BTC", interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor(() => fakeHttp.RequestCount >= 1, TimeSpan.FromSeconds(2));

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor(() => host.LastLogContains("OnUnmountAsync: flushed"), TimeSpan.FromSeconds(2));

        var snapshotAfterUnmount = fakeHttp.RequestCount;
        await Task.Delay(200);
        Assert.Equal(snapshotAfterUnmount, fakeHttp.RequestCount);
    }

    [Fact]
    public async Task HttpFailure_ReportsError_AndKeepsLoopAlive()
    {
        var fakeHttp = new FakeHttp { Handler = _ => throw new HttpRequestException("network down") };
        var http = new HttpClient(fakeHttp);
        var js = new FakeJsRuntime();
        var (host, _) = BuildHost(http, js, "BTC", interval: 30);

        host.RenderAsLiveRoot();
        await WaitFor(() => fakeHttp.RequestCount > 1, TimeSpan.FromSeconds(2));

        Assert.True(fakeHttp.RequestCount > 1, "expected the poll loop to keep retrying after a network error");
    }

    [Fact]
    public void CoinCapResponse_DeserializesCleanly()
    {
        const string json = """{"data":{"priceUsd":"65123.45","symbol":"BTC"}}""";
        var resp = JsonSerializer.Deserialize(json, LiveTickerJsonContext.Default.CoinCapResponse);
        Assert.NotNull(resp);
        Assert.Equal("65123.45", resp!.Data!.PriceUsd);
        Assert.Equal("BTC", resp.Data.Symbol);
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

    // --- Helpers ------------------------------------------------------------

    private static (TickerHost Host, RecordingHandle Handle) BuildHost(
        HttpClient http, IJSRuntime js, string symbol, int interval)
    {
        var handle = new RecordingHandle();
        var host = new TickerHost { Symbol = symbol, Interval = interval, RenderHandle = handle };
        host.SetServices(BuildServicesWith(http, js));
        return (host, handle);
    }

    private static IServiceProvider BuildServices() =>
        BuildServicesWith(new HttpClient(), new FakeJsRuntime());

    private static IServiceProvider BuildServicesWith(HttpClient http, IJSRuntime js)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton(http);
        sc.AddSingleton(js);
        return sc.BuildServiceProvider();
    }

    private static async Task WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(15);
        }
    }

    // Parent wrapper that conditionally hosts the LiveTicker through the
    // generator-emitted factory — that's the path that registers the child
    // with the framework so lifecycle hooks fire. DI for HttpClient + IJSRuntime
    // happens through the IServiceProvider the test passes to RenderAsLiveRoot.
    private sealed class TickerHost : Component
    {
        public List<string> Log { get; } = new();
        public string Symbol { get; set; } = "BTC";
        public int Interval { get; set; } = 100;
        public bool Mounted { get; set; } = true;

        // Pinned services so the host can be re-rendered with the same provider
        // across multiple RenderAsLiveRoot calls without the caller threading it
        // through every helper.
        private IServiceProvider _services = new ServiceCollection().BuildServiceProvider();
        public void SetServices(IServiceProvider services) => _services = services;

        public new string RenderAsLiveRoot() => base.RenderAsLiveRoot(_services);

        public bool LastLogContains(string fragment)
        {
            lock (Log)
            {
                for (var i = Log.Count - 1; i >= 0; i--)
                {
                    if (Log[i].Contains(fragment, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        protected override Component Render() =>
            Mounted
                ? LiveTicker(Symbol: Symbol, Interval: Interval, Log: AppendLog)
                : (Component)Fragment();

        private void AppendLog(string entry)
        {
            lock (Log)
            {
                Log.Add(entry);
            }
        }
    }
}

internal sealed class FakeHttp : HttpMessageHandler
{
    public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Handler { get; set; }
    public int RequestCount;

    public static (HttpClient Client, FakeHttp Handler) WithPrices(params (string Asset, decimal Price)[] prices)
    {
        var byAsset = prices.ToDictionary(p => p.Asset, p => p.Price, StringComparer.OrdinalIgnoreCase);
        var handler = new FakeHttp
        {
            Handler = req =>
            {
                var asset = req.RequestUri!.AbsolutePath.Split('/').Last();
                if (!byAsset.TryGetValue(asset, out var price))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                var body = $"{{\"data\":{{\"priceUsd\":\"{price.ToString(System.Globalization.CultureInfo.InvariantCulture)}\",\"symbol\":\"{asset}\"}}}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
                });
            }
        };
        return (new HttpClient(handler), handler);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref RequestCount);
        return Handler is null
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            : Handler(request);
    }
}

internal sealed class FakeJsRuntime : IJSRuntime
{
    private readonly ConcurrentBag<(string Identifier, object?[]? Args)> _calls = new();
    private readonly Dictionary<string, object?> _responses = new();

    public void SetResponse(string identifier, object? response) => _responses[identifier] = response;

    public IReadOnlyList<object?[]?> GetCalls(string identifier) =>
        _calls.Where(c => c.Identifier == identifier).Select(c => c.Args).ToArray();

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        _calls.Add((identifier, args));
        if (_responses.TryGetValue(identifier, out var canned) && canned is TValue typed)
        {
            return ValueTask.FromResult(typed);
        }

        // sessionStorage.getItem returns string? — default(string?) is null, which is the
        // "no stored history" case the component treats as a fresh start.
        return ValueTask.FromResult<TValue>(default!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);
}

internal sealed class RecordingHandle : IRenderHandle
{
    public int RequestRenderCount;
    public int RequestPublishRenderCount;

    public Task RequestRenderAsync()
    {
        Interlocked.Increment(ref RequestRenderCount);
        return Task.CompletedTask;
    }

    public Task RequestPublishRenderAsync()
    {
        Interlocked.Increment(ref RequestPublishRenderCount);
        return Task.CompletedTask;
    }
}
