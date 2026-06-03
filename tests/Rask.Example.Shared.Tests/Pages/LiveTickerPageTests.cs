using Rask.Core;
using Rask.Core.Routing;
using Rask.Example.Shared.Pages;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class LiveTickerPageTests
{
    [Theory]
    [InlineData("/realtime/BTC", "BTC")]
    [InlineData("/realtime/ETH", "ETH")]
    [InlineData("/realtime/SOL", "SOL")]
    public void RouteParam_Symbol_BindsFromUrl_AndRendersTitle(string path, string expectedSymbol)
    {
        var routeState = new RouteState { Path = path };
        var html = new Rask.Example.Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains($"{expectedSymbol} live ticker", html);
        Assert.Contains("ticker-symbol-switcher", html);
        Assert.Contains($"ticker-switch-{expectedSymbol}", html);
    }

    [Fact]
    public void Render_EmitsAllThreeSwitchButtons()
    {
        var routeState = new RouteState { Path = "/realtime/BTC" };
        var html = new Rask.Example.Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("ticker-switch-BTC", html);
        Assert.Contains("ticker-switch-ETH", html);
        Assert.Contains("ticker-switch-SOL", html);
    }

    [Fact]
    public void Render_HookActivityCard_Present()
    {
        var routeState = new RouteState { Path = "/realtime/BTC" };
        var html = new Rask.Example.Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        // The card may show the empty-hint OR an already-populated log depending on
        // how fast the child LiveTicker's lifecycle hooks fire during the first
        // RenderAsLiveRoot. Assert on the stable structural marker.
        Assert.Contains("Hook activity", html);
        Assert.Contains("ticker-clear-log", html);
    }
}
