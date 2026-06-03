using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Syntax-highlighting coverage. Highlighting is produced SERVER-SIDE by ColorCode
// (CodeSample tokenizes its C# Source into <span class="keyword|string|comment|…">
// markup injected via Raw()), so the highlighted spans are part of the very first
// render — there is no client-side highlight.js, no head <link>/<script>, and no
// async settle. These tests assert that every `pre code[class*=language-]` block
// carries token spans on first paint and after in-SPA navigation (the morph must
// not flatten the Raw spans), and that no highlight.js asset is requested.
public abstract partial class SharedSmokeTests
{
    // Highlighting lands in the first render, but on a cold WASM boot the whole page
    // (heading + code) only paints once the runtime has loaded; this ceiling lets the
    // span check ride out that boot on a constrained CI runner. On a warm run the spans
    // are present immediately and WaitForHighlightedSpansAsync returns early.
    protected const int HighlightSettleTimeoutMs = 35_000;

    [Fact]
    public Task Highlight_FirstLoad_ValidationPage_HighlightsEveryCodeBlock() => RunAsync(async () =>
    {
        // /validation embeds many CodeSample instances. On first load every
        // `pre code[class*=language-]` block must contain ColorCode token spans —
        // that's the visible signal the server-side highlighter ran.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: HighlightSettleTimeoutMs);
    });

    [Fact]
    public Task Highlight_FirstLoad_NoHljsAssetsInHead() => RunAsync(async () =>
    {
        // Highlighting moved server-side, so the old highlight.js <link>/<script>
        // must no longer be contributed to <head> by any CodeSample.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var styleCount = await Page.Locator("head link[href*='highlight']").CountAsync();
        var scriptCount = await Page.Locator("head script[src*='highlight']").CountAsync();
        Assert.Equal(0, styleCount);
        Assert.Equal(0, scriptCount);
    });

    [Fact]
    public Task Highlight_AfterCrossPageNavigation_StillHighlightsOnDestination() => RunAsync(async () =>
    {
        // Highlighting must be present on a SECOND page reached by in-SPA navigation:
        // the destination's render ships the highlighted <code> and the morph must
        // apply the Raw spans, not flatten them to plain text.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: HighlightSettleTimeoutMs);

        await ClickSidebar("Routing");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Routing",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: HighlightSettleTimeoutMs);
    });

    [Fact]
    public Task Highlight_AfterRoundTripNav_RehighlightsOnReturn() => RunAsync(async () =>
    {
        // /validation → / (no CodeSamples) → /validation. On the return visit every
        // code block must carry token spans again — guards against the morph dropping
        // the Raw spans when a CodeSample page is re-entered.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: HighlightSettleTimeoutMs);

        await ClickSidebar("Welcome");
        await Expect(Page.Locator("h1.display-5")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: HighlightSettleTimeoutMs);
    });

    [Fact]
    public Task Highlight_RapidNavBetweenCodeSamplePages_AlwaysSettlesHighlighted() => RunAsync(async () =>
    {
        // Stress-tests highlighting under rapid in-SPA navigation. After a chain of
        // sidebar clicks across multiple CodeSample-heavy pages, the final page must
        // end up with every code block highlighted (no morph dropped the spans).
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await ClickSidebar("Routing");
        await ClickSidebar("Navigator");
        await ClickSidebar("Complex models");
        await ClickSidebar("HttpClient + DI");

        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("HttpClient + DI",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: HighlightSettleTimeoutMs);
    });

    private async Task AssertAllCodeBlocksHighlightedAsync(int timeoutMs)
    {
        // ColorCode wraps tokens in <span class="…"> inside the <code class="language-*">.
        // We wait until every code element with a language-* class contains at least one
        // token span — that's the contract of the server-side highlighter.
        await WaitForHighlightedSpansAsync(timeoutMs);

        var total = await Page.Locator("pre code[class*='language-']").CountAsync();
        var highlighted = await Page.Locator("pre code[class*='language-']:has(span[class])").CountAsync();
        Assert.True(total > 0,
            $"No code blocks found on the page — selector mismatch? [{await HighlightDiagnosticsAsync()}]");
        Assert.True(total == highlighted,
            $"Only {highlighted}/{total} code blocks carried token spans after {timeoutMs}ms. " +
            $"[{await HighlightDiagnosticsAsync()}]");
    }

    // Snapshot of the highlight state, surfaced in assert messages so a CI failure
    // distinguishes a slow cold WASM boot (no blocks rendered yet) from a genuine
    // regression (blocks present but carrying no token spans).
    protected Task<string> HighlightDiagnosticsAsync() => Page.EvaluateAsync<string>(
        "() => { const all = Array.from(document.querySelectorAll('pre code[class*=\"language-\"]')); " +
        "const hl = all.filter(c => c.querySelector('span[class]')).length; " +
        "return `blocks=${all.length} withSpans=${hl}`; }");

    // Shared by the deep-diagnostics tests in ExampleSmokeTests.HighlightJs.cs too.
    protected async Task WaitForHighlightedSpansAsync(int timeoutMs)
    {
        // Poll until every targeted code block carries at least one token span. On a warm
        // run this is true on the first tick; the loop only bites on a cold WASM boot.
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var settled = await Page.EvaluateAsync<bool>(
                "() => { const all = Array.from(document.querySelectorAll('pre code[class*=\"language-\"]')); " +
                "return all.length > 0 && all.every(c => c.querySelector('span[class]') !== null); }");
            if (settled)
            {
                return;
            }

            await Task.Delay(150);
        }
    }
}
