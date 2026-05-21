using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// highlight.js — deeper diagnostics requiring deep-link or MutationObserver
// access. Runs on Server + Wasm.Host (the SPA-fallback hosts) only. The
// StandaloneWasm collection inherits the simpler tests in
// SharedSmokeTests.HighlightJs.cs.
public abstract partial class ExampleSmokeTests
{
    [Fact]
    public Task Highlight_OnRenderedHook_IsInvokedOnEveryCodeSamplePageMount() => RunAsync(async () =>
    {
        // Direct instrumentation of the hook contract. We install an init
        // script that wraps Rask.CodeSample.rendered BEFORE the first page
        // load, so every invocation across every nav is captured. After a
        // round-trip through three pages we assert the wrapper saw at least
        // three calls — one per page mount. If the count is < expected, the
        // hook is being skipped on subsequent mounts (the Wasm bug). If the
        // count is 0, the hook is never firing at all (the Server bug).
        await Page.AddInitScriptAsync(@"
            (() => {
                window.__raskHljsCalls = [];
                const w = window;
                function instrument() {
                    if (!w.Rask) return setTimeout(instrument, 25);
                    const orig = w.Rask.CodeSample && w.Rask.CodeSample.rendered;
                    if (!w.Rask.CodeSample) {
                        w.Rask.CodeSample = { rendered: function(...args) {
                            window.__raskHljsCalls.push({ ts: Date.now(), args });
                        }};
                        return;
                    }
                    if (orig.__instrumented) return;
                    const wrapped = function(...args) {
                        window.__raskHljsCalls.push({ ts: Date.now(), args });
                        return orig.apply(this, args);
                    };
                    wrapped.__instrumented = true;
                    w.Rask.CodeSample.rendered = wrapped;
                }
                instrument();
            })();
        ");

        await Page.GotoAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(500);

        await ClickSidebar("Routing");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Routing",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(500);

        await ClickSidebar("Navigator");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Navigator",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(500);

        var calls = await Page.EvaluateAsync<int>("() => (window.__raskHljsCalls || []).length");
        Assert.True(calls >= 3,
            $"Expected at least 3 hook invocations (one per page mount), saw {calls}. " +
            "0 calls = hook never fires (Server regression). " +
            "1 call = hook only fires on first SPA mount (Wasm regression).");
    });

    [Fact]
    public Task Highlight_HeadScripts_NoDuplicateAfterMultipleNavs() => RunAsync(async () =>
    {
        // Head dedup invariant: navigating across multiple CodeSample pages
        // must not stack hljs <link>/<script> entries. A duplicate is
        // observable (double apply()), forces double network round-trips, and
        // bloats the head.
        await Page.GotoAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await ClickSidebar("Routing");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Routing",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await ClickSidebar("Navigator");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Navigator",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await ClickSidebar("Complex models");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Complex models",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var styleCount = await Page.Locator("head link[href*='highlight']").CountAsync();
        var scriptCount = await Page.Locator("head script[src*='highlight.min.js']").CountAsync();
        Assert.Equal(1, styleCount);
        Assert.Equal(1, scriptCount);
    });

    [Fact]
    public Task Highlight_DeepLinkToCodeSamplePage_HighlightsOnFirstPaint() => RunAsync(async () =>
    {
        // Tests every CodeSample page in isolation — direct deep-link, fresh
        // session, no prior in-SPA state. Catches "highlight only works after
        // navigation from another page" (which would be the inverse of the
        // user's Wasm bug, but worth ruling out).
        var pages = new[]
        {
            ("/validation", "Validation"),
            ("/routing", "Routing"),
            ("/navigator", "Navigator"),
            ("/nested-forms", "Complex models"),
            ("/http", "HttpClient + DI")
        };

        foreach (var (path, heading) in pages)
        {
            await Page.GotoAsync(path);
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync(heading,
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

            await WaitForHljsAsync(timeoutMs: 10_000);
            var total = await Page.Locator("pre code[class*='language-']").CountAsync();
            var highlighted = await Page.Locator("pre code.hljs[class*='language-']").CountAsync();
            Assert.True(total > 0, $"{path}: no code blocks found.");
            Assert.True(highlighted == total,
                $"{path}: only {highlighted}/{total} blocks highlighted on first paint.");
        }
    });

    private async Task WaitForHljsAsync(int timeoutMs)
    {
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
