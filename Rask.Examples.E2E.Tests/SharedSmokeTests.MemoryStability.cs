using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Client-side memory stability. Runs on Server, Wasm.Host, and StandaloneWasm.
//
// We sample performance.memory.usedJSHeapSize before and after a stress loop
// (many in-SPA navigations) and assert the heap doesn't balloon. The browser
// reports approximate values in headless Chromium without --enable-precise-
// memory-info, but a real leak (uncancelled subscriptions, retained component
// trees, leaked event handlers) shows up as orders-of-magnitude growth — well
// above the noise floor.
//
// Production goal: lower memory footprint than Blazor. Tests assert *absolute*
// bounds so a regression to "still works but uses 10x" still fails.
public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task Memory_MultipleNavigations_JsHeapStaysBounded() => RunAsync(async () =>
    {
        // Baseline → 30 round-trip navs → final. Assert final isn't more than
        // 3x baseline AND isn't above an absolute hard cap (200 MB) — that hard
        // cap catches WASM-side runaway allocation that a ratio test might
        // miss when baseline is already huge.
        await NavigateToAsync("/");
        await Expect(Page.Locator("h1.display-5")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(500);

        var baseline = await SampleJsHeapAsync();

        var routes = new[] { "Events", "Two-way binding", "Scoped CSS", "Routing", "Welcome" };
        for (var i = 0; i < 6; i++)
        {
            foreach (var label in routes)
            {
                await ClickSidebar(label);
                await Page.WaitForTimeoutAsync(150);
            }
        }

        // Encourage browser GC before sampling. Playwright's evaluate runs
        // synchronously on the page so timing is reliable.
        await Page.EvaluateAsync(
            "() => new Promise(r => { if (window.gc) { window.gc(); } setTimeout(r, 200); })");

        var after = await SampleJsHeapAsync();

        Assert.True(after > 0, $"No heap reading available on this browser. baseline={baseline} after={after}");
        Assert.True(after < baseline * 3 + 25_000_000,
            $"JS heap grew unexpectedly. baseline={baseline:N0} after={after:N0} bytes. " +
            "Suspect: leaked component subscriptions, retained event handlers, or uncancelled lifecycle hooks.");

        // Absolute hard cap: 250 MB. WASM baseline runs ~30-60 MB; Server baseline
        // ~10-20 MB. A test that lands near 250 MB is broken regardless of ratio.
        Assert.True(after < 250_000_000,
            $"JS heap absolute cap exceeded. after={after:N0} bytes (cap 250 MB).");
    });

    [Fact]
    public Task Memory_DownAndUpNavToCodeSamplePages_NoUnboundedGrowth() => RunAsync(async () =>
    {
        // Specific to the highlight.js regression class: bouncing between
        // CodeSample-heavy pages must not leak. Each page mounts ~15 CodeSample
        // instances, each subscribing to a JS invocation. If OnRenderedAsync
        // closures, the IJSRuntime call queue, or the head-asset cache leak,
        // this test catches it.
        await NavigateToAsync("/");
        await Expect(Page.Locator("h1.display-5")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(500);
        var baseline = await SampleJsHeapAsync();

        var labels = new[] { "Validation", "Routing", "Navigator", "Complex models", "HttpClient + DI" };
        for (var i = 0; i < 5; i++)
        {
            foreach (var label in labels)
            {
                await ClickSidebar(label);
                await Page.WaitForTimeoutAsync(120);
            }
        }

        await Page.EvaluateAsync(
            "() => new Promise(r => { if (window.gc) { window.gc(); } setTimeout(r, 200); })");

        var after = await SampleJsHeapAsync();

        Assert.True(after < baseline * 4 + 30_000_000,
            $"Heap grew across CodeSample-heavy navs. baseline={baseline:N0} after={after:N0}. " +
            "Likely culprit: hljs <link>/<script> head dedup leak OR CodeSample lifecycle closures.");
    });

    private async Task<long> SampleJsHeapAsync()
    {
        // performance.memory is Chromium-only. Returns null on other browsers,
        // but our fixture is Chromium-only so we treat null as a test bug
        // rather than a skip.
        return await Page.EvaluateAsync<long>(
            "() => (performance.memory && performance.memory.usedJSHeapSize) || 0");
    }
}
