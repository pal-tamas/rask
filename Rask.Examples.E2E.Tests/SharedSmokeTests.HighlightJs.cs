using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// highlight.js coverage — documents two user-reported regressions:
//
//   Bug #1 (Server):  highlight.js never runs. No code block ever gets the
//                     .hljs class on Rask.Example.Server.
//   Bug #2 (Wasm):    highlight.js runs on first load but stops working after
//                     in-SPA navigation to any other CodeSample-bearing page.
//
// CodeSample.OnRenderedAsync invokes `Rask.CodeSample.rendered` via IJSRuntime
// — both bugs trace back to that hook's behaviour across hosts and across
// navigation. These tests will FAIL on a regressed host; that's the point —
// they document the bug then drive the fix.
public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task Highlight_FirstLoad_ValidationPage_HighlightsEveryCodeBlock() => RunAsync(async () =>
    {
        RequireAssetEndpoint();
        // /validation embeds 15 CodeSample instances. On first load every
        // `pre code[class*=language-]` block must end up with the .hljs class
        // applied — that's the visible signal hljs ran. Bug #1 (Server) makes
        // this fail because the hook never reaches the JS runtime.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: 10_000);
    });

    [Fact]
    public Task Highlight_FirstLoad_HljsAssetsLandInHead() => RunAsync(async () =>
    {
        // The framework auto-emits CodeSample.Head into the page <head>. If the
        // hljs stylesheet or core script isn't reaching head on a given host,
        // hljs is undefined when CodeSample.rendered runs and the highlight is
        // a no-op. This test isolates the head-asset emission from the hook
        // invocation, so a failure here means head contribution is broken.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var styleCount = await Page.Locator("head link[href*='highlight']").CountAsync();
        var scriptCount = await Page.Locator("head script[src*='highlight.min.js']").CountAsync();
        Assert.True(styleCount >= 1, $"Expected hljs stylesheet in head; found {styleCount}.");
        Assert.True(scriptCount >= 1, $"Expected hljs script in head; found {scriptCount}.");
    });

    [Fact]
    public Task Highlight_AfterCrossPageNavigation_StillHighlightsOnDestination() => RunAsync(async () =>
    {
        RequireAssetEndpoint();
        // The Wasm-specific bug: hljs works on first load but breaks on the
        // SECOND page. We start on /validation (confirm hljs works there),
        // then sidebar-nav to /routing — a different page with CodeSamples —
        // and demand hljs runs there too. If OnRenderedAsync only fires on
        // the very first SPA mount, this test catches the regression.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: 10_000);

        await ClickSidebar("Routing");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Routing",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: 10_000);
    });

    [Fact]
    public Task Highlight_AfterRoundTripNav_RehighlightsOnReturn() => RunAsync(async () =>
    {
        RequireAssetEndpoint();
        // /validation → / (no CodeSamples) → /validation. On the return visit,
        // every code block must be highlighted again. A regression where the
        // hook only fires on the very first mount in a SPA session would let
        // the initial visit pass but break the return — this test discriminates
        // between "fires on first mount only" and "fires on every mount".
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: 10_000);

        await ClickSidebar("Welcome");
        await Expect(Page.Locator("h1.display-5")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: 10_000);
    });

    [Fact]
    public Task Highlight_RapidNavBetweenCodeSamplePages_AlwaysSettlesHighlighted() => RunAsync(async () =>
    {
        RequireAssetEndpoint();
        // Stress-tests the hook under rapid in-SPA navigation. After a chain
        // of sidebar clicks across multiple CodeSample-heavy pages, the final
        // page must end up fully highlighted. Catches "the last nav cancelled
        // a pending hljs apply()" or "the hook queued behind a render and was
        // dropped" races.
        await NavigateToAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await ClickSidebar("Routing");
        await ClickSidebar("Navigator");
        await ClickSidebar("Complex models");
        await ClickSidebar("HttpClient + DI");

        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("HttpClient + DI",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await AssertAllCodeBlocksHighlightedAsync(timeoutMs: 15_000);
    });

    private async Task AssertAllCodeBlocksHighlightedAsync(int timeoutMs)
    {
        // hljs adds the .hljs class on the same <code> element that already
        // has the .language-* class. We wait until every code element with a
        // language-* class also has .hljs — that's the contract of
        // highlightElement. If hljs never runs, the counts diverge.
        await WaitForHljsAsync(timeoutMs);

        var total = await Page.Locator("pre code[class*='language-']").CountAsync();
        var highlighted = await Page.Locator("pre code.hljs[class*='language-']").CountAsync();
        Assert.True(total > 0, "No code blocks found on the page — selector mismatch?");
        Assert.Equal(total, highlighted);
    }

    private async Task WaitForHljsAsync(int timeoutMs)
    {
        // Poll until every targeted code block carries .hljs. We use a
        // page-side function to avoid round-tripping per-element queries.
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var settled = await Page.EvaluateAsync<bool>(
                "() => { const all = Array.from(document.querySelectorAll('pre code[class*=\"language-\"]')); " +
                "return all.length > 0 && all.every(c => c.classList.contains('hljs')); }");
            if (settled)
            {
                return;
            }

            await Task.Delay(150);
        }
    }
}
