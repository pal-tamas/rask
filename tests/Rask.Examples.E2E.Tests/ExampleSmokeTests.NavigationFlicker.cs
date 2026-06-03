using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// LiveTicker-based navigation-flicker tests. Live in ExampleSmokeTests because
// StandaloneWasm's NavigateToAsync awaits window.location matching the target
// path — that's flaky for /realtime/{Symbol} on WasmAppHost.
public abstract partial class ExampleSmokeTests
{
    [Fact]
    public Task Nav_LiveTickerSymbolSwitch_UpdatesSvgChart() => RunAsync(async () =>
    {
        // /realtime/BTC → /realtime/ETH (in-page nav via switcher button).
        // The chart is a server-rendered SVG (no canvas, no JS): a single <svg>
        // must remain in #ticker-chart after the switch, redrawn for the new
        // symbol from the same transport as the rest of the page.
        await NavigateToAsync("/realtime/BTC");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Wait for the first tick to render the SVG chart.
        await Expect(Page.Locator("#ticker-chart svg")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        await Page.Locator("#ticker-switch-ETH").ClickAsync();
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("ETH",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        // The new symbol's chart renders as a single SVG; never a <canvas>.
        await Expect(Page.Locator("#ticker-chart svg")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        Assert.Equal(1, await Page.Locator("#ticker-chart svg").CountAsync());
        Assert.Equal(0, await Page.Locator("#ticker-chart canvas").CountAsync());
    });

    // Regression (head-morph node identity): navigating away from a page must MOVE
    // unchanged head siblings rather than destroy + recreate them — removing a
    // stylesheet drops its rules immediately and produces a visible flicker. The fix
    // emits a stable data-rask-key on every head asset so the morph matches by identity.
    // This stamps a JS marker on the Bootstrap link before nav and asserts it survives.
    [Fact]
    public Task Nav_AwayFromLiveTicker_PreservesHeadAssetNodeIdentity() => RunAsync(async () =>
    {
        await NavigateToAsync("/realtime/BTC");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Stamp a marker on the Bootstrap CSS link (present on every page via App.Head).
        // It must survive nav — the morph should move it, not recreate it.
        var stamped = await Page.EvaluateAsync<bool>(@"() => {
            var bs = document.querySelector('link[href*=""bootstrap/bootstrap.min.css""]');
            if (!bs) return false;
            bs.__raskFlickerProbe = 'bootstrap';
            return true;
        }");
        Assert.True(stamped, "Pre-nav probe (bootstrap link) not found in <head>.");

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
