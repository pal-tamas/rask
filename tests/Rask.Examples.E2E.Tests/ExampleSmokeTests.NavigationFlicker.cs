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

    // Regression: navigating away from /realtime/BTC produced a visible
    // flicker — LiveTicker's chart.js Head contribution disappeared from the
    // registry, every later head sibling shifted by one slot, and the morph's
    // positional walk hit tag-name mismatches and REPLACED nodes (including
    // the Bootstrap CSS link and, on the Server runtime, the scoped-css link).
    // Removing a stylesheet drops its rules immediately → unstyled page.
    //
    // The fix emits a stable data-rask-key on every head asset so the morph's
    // keyed branch matches by identity. This test stamps a JS marker on the
    // Bootstrap link before nav and asserts the marker survives — i.e., the
    // morph moved the link rather than destroying and recreating it.
    [Fact]
    public Task Nav_AwayFromLiveTicker_PreservesHeadAssetNodeIdentity() => RunAsync(async () =>
    {
        await NavigateToAsync("/realtime/BTC");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Stamp a marker on the Bootstrap CSS link (present on every page via App.Head)
        // and on the chart.js script (present only on the LiveTicker page). The
        // Bootstrap link must survive nav; the chart.js script must be cleanly removed.
        var stamped = await Page.EvaluateAsync<bool>(@"() => {
            var bs = document.querySelector('link[href*=""bootstrap/bootstrap.min.css""]');
            var chart = document.querySelector('script[src*=""chart.umd.js""]');
            if (!bs || !chart) return false;
            bs.__raskFlickerProbe = 'bootstrap';
            chart.__raskFlickerProbe = 'chart';
            return true;
        }");
        Assert.True(stamped, "Pre-nav probes (bootstrap link + chart script) not found in <head>.");

        await ClickSidebar("Lifecycle");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Lifecycle hooks",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var preserved = await Page.EvaluateAsync<bool>(@"() => {
            var bs = document.querySelector('link[href*=""bootstrap/bootstrap.min.css""]');
            return !!(bs && bs.__raskFlickerProbe === 'bootstrap');
        }");
        Assert.True(preserved,
            "Bootstrap <link> was replaced during nav from LiveTicker — head morph displaced " +
            "an unchanged stylesheet, which drops its rules and produces a visible flicker.");

        var chartGone = await Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('script[src*=\"chart.umd.js\"]').length");
        Assert.Equal(0, chartGone);
    });

    // Regression: highlighting is rendered server-side as ColorCode token <span>s
    // inside the <code class="language-csharp"> block. Subsequent server-driven
    // re-renders ship the same highlighted markup, so the morph must preserve those
    // spans rather than flatten them to plain text. A re-render burst (from
    // LifecyclePage's "Mount probe", whose OnMountAsync awaits then emits another
    // render) must leave the code block highlighted with its span count intact.
    [Fact]
    public Task Nav_LifecyclePageRerender_KeepsCodeBlockHighlighted() => RunAsync(async () =>
    {
        await NavigateToAsync("/lifecycle");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Lifecycle hooks",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Expect(Page.Locator(".sample-card code.language-csharp span.keyword").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        var initialSpans = await Page.Locator(".sample-card code.language-csharp span").CountAsync();
        Assert.True(initialSpans > 0, "Expected token spans before re-render trigger.");

        // Trigger a re-render burst. The probe's OnMountAsync awaits 450 ms,
        // then emits another render — the morph must not strip the Raw spans.
        await Page.Locator("#lifecycle-cycle-mount").ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        // End state must still be highlighted and span count preserved.
        await Expect(Page.Locator(".sample-card code.language-csharp"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        var finalSpans = await Page.Locator(".sample-card code.language-csharp span").CountAsync();
        Assert.Equal(initialSpans, finalSpans);
    });
}
