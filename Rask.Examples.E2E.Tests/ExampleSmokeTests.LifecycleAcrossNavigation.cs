using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// LiveTicker-based lifecycle correctness tests. Live here rather than in
// SharedSmokeTests because StandaloneWasm's NavigateToAsync asserts on
// window.location matching the target path — that assertion is flaky for
// /realtime/{Symbol} on WasmAppHost in CI environments where pushState lands
// slightly after the framework's RouteState swap. Server and Wasm.Host (the
// ExampleSmokeTests-bearing hosts) have no such restriction.
public abstract partial class ExampleSmokeTests
{
    [Fact]
    public Task Lifecycle_FirstMount_FiresAllHooksInOrder() => RunAsync(async () =>
    {
        // Verifies the canonical first-mount sequence:
        //   OnMount → OnMountAsync → OnPropsChanged → OnPropsChangedAsync →
        //   Render → OnRendered(firstRender:true) → OnRenderedAsync(firstRender:true)
        // If any one of these is missing or out of order on a host, the test
        // fails and we know exactly where the framework drifted.
        await NavigateToAsync("/realtime/BTC");

        var log = Page.Locator("#ticker-log");
        await Expect(log).ToContainTextAsync("OnMount: requesting persisted history",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Expect(log).ToContainTextAsync("OnPropsChanged: initial Symbol=BTC",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(log).ToContainTextAsync("OnRendered(firstRender:true)",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(log).ToContainTextAsync("OnRenderedAsync(firstRender:true): Chart.js initialised",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        // Order check: OnMount before OnPropsChanged before OnRendered.
        var entries = await log.AllInnerTextsAsync();
        var combined = string.Join("\n", entries);
        var mountIdx = combined.IndexOf("OnMount:", StringComparison.Ordinal);
        var propsIdx = combined.IndexOf("OnPropsChanged:", StringComparison.Ordinal);
        var renderedIdx = combined.IndexOf("OnRendered(firstRender:true)", StringComparison.Ordinal);
        Assert.True(mountIdx >= 0 && propsIdx > mountIdx && renderedIdx > propsIdx,
            $"Hook order broken. mount={mountIdx} props={propsIdx} rendered={renderedIdx}.\nLog:\n{combined}");
    });

    [Fact]
    public Task Lifecycle_OnPropsChanged_FiresWithoutRemountingOnPropChange() => RunAsync(async () =>
    {
        // The framework caches a routed page instance when navigating to the same
        // route type with a different [RouteParam] value (here Symbol). Hitting
        // /realtime/BTC then /realtime/ETH must reuse the SAME LiveTickerPage
        // instance — the LiveTicker child is re-created when its parent prop
        // (Symbol) changes, but the PAGE survives, so the page-owned log keeps
        // BTC's mount entry plus the new ETH transition entries.
        await NavigateToAsync("/realtime/BTC");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Expect(Page.Locator("#ticker-log")).ToContainTextAsync(
            "OnMount: requesting persisted history for BTC",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await Page.Locator("#ticker-switch-ETH").ClickAsync();
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("ETH",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        var log = Page.Locator("#ticker-log");
        await Expect(log).ToContainTextAsync("OnPropsChanged: Symbol BTC → ETH",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(log).ToContainTextAsync("OnPropsChangedAsync: switched to ETH",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // No second OnMount entry for ETH — the page instance was cached.
        var combined = string.Join("\n", await log.AllInnerTextsAsync());
        Assert.DoesNotContain("OnMount: requesting persisted history for ETH", combined);
    });

    [Fact]
    public Task Lifecycle_OnRenderedAsync_FiresAgainAfterPropChange() => RunAsync(async () =>
    {
        // Regression test for the highlight.js Wasm bug: OnRenderedAsync MUST
        // fire again when a prop changes — otherwise scoped JS that runs from
        // this hook (CodeSample.rendered, LiveTicker.draw) stops working as
        // soon as anything in the URL or props shifts.
        await NavigateToAsync("/realtime/BTC");
        var log = Page.Locator("#ticker-log");
        await Expect(log).ToContainTextAsync("OnRenderedAsync(firstRender:true): Chart.js initialised",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });

        // Switch to ETH and assert the chart instance still exists.
        await Page.Locator("#ticker-switch-ETH").ClickAsync();
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("ETH",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        await WaitForAsync(async () =>
        {
            var hasChart = await Page.EvaluateAsync<bool>(
                "() => { const c = document.querySelector('canvas[data-rask-ticker]'); " +
                "return !!(c && c.__raskChart); }");
            return hasChart;
        }, timeoutMs: 10_000, "Chart instance never attached to canvas after Symbol switch");
    });

    [Fact]
    public Task Lifecycle_NavigateAway_FiresUnmountHooksBeforePageGone() => RunAsync(async () =>
    {
        // Unmount semantics: when LiveTickerPage navigates away, its LiveTicker
        // child must run OnUnmount + OnUnmountAsync cleanly. The poll loop's
        // `await Task.Delay(IntervalMs, ct)` must observe ct.IsCancellationRequested
        // and exit via the OperationCanceledException catch — no log of
        // "lifecycle hook on LiveTicker faulted" should appear.
        await NavigateToAsync("/realtime/BTC");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Expect(Page.Locator("#ticker-log")).ToContainTextAsync(
            "OnMountAsync: loaded",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        await ClickSidebar("Welcome");
        await Expect(Page.Locator("h1.display-5")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await Page.WaitForTimeoutAsync(500);
        Assert.DoesNotContain("lifecycle hook on LiveTicker faulted", ServerLog);
        Assert.DoesNotContain("lifecycle hook on LiveTickerPage faulted", ServerLog);
    });

    [Fact]
    public Task Lifecycle_RoundTripNavigation_NewPageInstanceMountsCleanly() => RunAsync(async () =>
    {
        // /realtime/BTC → away → /realtime/BTC: the second visit must boot
        // fully (Chart.js initialised, hook log shows OnMount + OnRenderedAsync
        // again). If the framework left state behind from the prior page
        // instance, the second mount would either log "lifecycle hook faulted"
        // or fail to produce a Chart.js instance.
        await NavigateToAsync("/realtime/BTC");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Expect(Page.Locator("#ticker-log")).ToContainTextAsync(
            "OnRenderedAsync(firstRender:true): Chart.js initialised",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        await ClickSidebar("Welcome");
        await Expect(Page.Locator("h1.display-5")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await NavigateToAsync("/realtime/BTC");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Fresh log on the second visit must contain a brand-new mount entry
        // AND a brand-new OnRenderedAsync firstRender:true entry.
        var log = Page.Locator("#ticker-log");
        await Expect(log).ToContainTextAsync("OnMount: requesting persisted history for BTC",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(log).ToContainTextAsync("OnRenderedAsync(firstRender:true): Chart.js initialised",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        // Chart instance attached to the freshly-rendered canvas.
        await WaitForAsync(async () =>
        {
            var hasChart = await Page.EvaluateAsync<bool>(
                "() => { const c = document.querySelector('canvas[data-rask-ticker]'); " +
                "return !!(c && c.__raskChart); }");
            return hasChart;
        }, timeoutMs: 15_000, "Second-visit chart never attached — likely orphaned state from prior page instance");

        Assert.DoesNotContain("lifecycle hook on LiveTicker faulted", ServerLog);
        Assert.DoesNotContain("lifecycle hook on LiveTickerPage faulted", ServerLog);
    });

    private static async Task WaitForAsync(Func<Task<bool>> predicate, int timeoutMs, string message)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException($"Timed out after {timeoutMs} ms waiting for: {message}");
    }
}
