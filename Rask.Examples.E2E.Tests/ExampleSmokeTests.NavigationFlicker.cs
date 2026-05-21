using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// LiveTicker-based navigation-flicker tests. Live in ExampleSmokeTests because
// StandaloneWasm's NavigateToAsync awaits window.location matching the target
// path — that's flaky for /realtime/{Symbol} on WasmAppHost.
public abstract partial class ExampleSmokeTests
{
    [Fact]
    public Task Nav_LiveTickerSymbolSwitch_ReusesCanvasInstance() => RunAsync(async () =>
    {
        // /realtime/BTC → /realtime/ETH (in-page nav via switcher button).
        // The canvas[data-rask-ticker] must remain a single element, and a
        // chart instance must be attached after the switch. A regression that
        // destroys + recreates the chart on every prop change would cause
        // visible chart flicker.
        await NavigateToAsync("/realtime/BTC");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Wait for chart to render.
        await Page.WaitForTimeoutAsync(1500);

        // Switch — same canvas should keep its __raskChart instance.
        await Page.Locator("#ticker-switch-ETH").ClickAsync();
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("ETH",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
        await Page.WaitForTimeoutAsync(500);

        var hasChartAfter = await Page.EvaluateAsync<bool>(
            "() => { const c = document.querySelector('canvas[data-rask-ticker]'); return !!(c && c.__raskChart); }");

        var canvasCount = await Page.Locator("canvas[data-rask-ticker]").CountAsync();
        Assert.Equal(1, canvasCount);
        Assert.True(hasChartAfter, "Chart instance not attached after Symbol switch.");
    });

    [Fact]
    public Task Nav_HeadAssets_NoDuplicateChartScriptAfterMultipleNavs() => RunAsync(async () =>
    {
        await NavigateToAsync("/realtime/BTC");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await ClickSidebar("Two-way binding");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Two-way binding",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await ClickSidebar("Live ticker");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var chartScripts = await Page.Locator("head script[src*='chart']").CountAsync();
        Assert.Equal(1, chartScripts);
    });
}
