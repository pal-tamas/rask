using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// LiveTicker-based render-count tests. Live in ExampleSmokeTests because
// StandaloneWasm's NavigateToAsync awaits window.location matching
// /realtime/{Symbol} which is flaky on WasmAppHost.
public abstract partial class ExampleSmokeTests
{
    [Fact]
    public Task RenderCount_LiveTickerPage_NoRunawayRendersIdle() => RunAsync(async () =>
    {
        // LiveTicker's poll loop in OnMountAsync produces a re-render every
        // poll iteration (default 3s). What it MUST NOT do is render more than
        // once per poll cycle. We sample the hook log size before/after a
        // short idle window: the log should grow by a bounded number of
        // entries (poll + render — order of 1-3 per poll), not exponentially.
        await NavigateToAsync("/realtime/BTC");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("BTC live ticker",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Expect(Page.Locator("#ticker-log")).ToContainTextAsync("OnRendered(firstRender:true)",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        var entriesBefore = await Page.Locator("#ticker-log li").CountAsync();

        // Idle through one poll cycle (~3s) plus a buffer.
        await Page.WaitForTimeoutAsync(4500);

        var entriesAfter = await Page.Locator("#ticker-log li").CountAsync();
        var delta = entriesAfter - entriesBefore;

        // A handful of log entries per poll is fine. > 20 in a single ~3s window
        // indicates a runaway render loop.
        Assert.True(delta < 20,
            $"Suspect render loop on LiveTickerPage: {delta} log entries added in 4.5s window.");
    });
}
