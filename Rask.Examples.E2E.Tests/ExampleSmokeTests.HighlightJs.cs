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

    [Fact]
    public Task Highlight_BrowserRefreshOnCodeSamplePage_DoesNotTripErrorBoundary() => RunAsync(async () =>
    {
        // User-reported regression (GitHub Pages WASM example):
        // After a successful first paint of /validation, hitting browser refresh
        // ("Reload") raises a JSException — "undefined is not an object
        // (evaluating 'window.hljs.highlightElement')" — that bubbles into the
        // RootErrorBoundary and replaces the page with the DefaultErrorPage
        // ("Something went wrong"). The hljs <script> contributed by every
        // CodeSample.Head should still flow through the Rask.* invoke gate on
        // a refresh exactly the same as on a first paint, so the OnRenderedAsync
        // call into Rask.CodeSample.rendered must wait until window.hljs is
        // defined before it runs.
        //
        // Reproduction shape: navigate to /validation, wait for highlight to
        // settle so hljs is provably cached, then Page.ReloadAsync(). After the
        // reload the page must still render the Validation heading (not the
        // boundary's "Something went wrong") and re-highlight every code block.
        await Page.GotoAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await WaitForHljsAsync(timeoutMs: 10_000);

        await Page.ReloadAsync();

        // Hard fail if the boundary fired — the error message is what the user
        // is actually staring at when they hit this regression.
        var boundary = await Page.Locator(".rask-error-boundary h1:has-text(\"Something went wrong\")").CountAsync();
        Assert.True(boundary == 0,
            "RootErrorBoundary tripped on refresh — likely a JSException from " +
            "CodeSample.OnRenderedAsync hitting `window.hljs.highlightElement` " +
            "before the hljs script's load event opened the Rask.* invoke gate.");

        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await WaitForHljsAsync(timeoutMs: 10_000);
        var total = await Page.Locator("pre code[class*='language-']").CountAsync();
        var highlighted = await Page.Locator("pre code.hljs[class*='language-']").CountAsync();
        Assert.True(total > 0, "/validation after refresh: no code blocks found.");
        Assert.Equal(total, highlighted);
    });

    [Fact]
    public Task Highlight_HljsScriptFails_DoesNotCrashIntoErrorBoundary() => RunAsync(async () =>
    {
        // Underlying failure mode behind the refresh report. When the hljs
        // <script> errors out (CDN flake, blocked by an extension, evicted /
        // corrupt cache entry on refresh, integrity mismatch, CSP), the
        // head-asset gate currently drains its queued Rask.* invokes anyway —
        // `done()` is attached to BOTH 'load' and 'error' — and the queued
        // `Rask.CodeSample.rendered` call then dereferences a still-undefined
        // `window.hljs`. The OnRenderedAsync task faults, the framework's
        // RootErrorBoundary trips, and the user sees "Something went wrong"
        // instead of just an un-highlighted code block (which would be the
        // expected graceful degradation).
        //
        // We force the failure deterministically by aborting every hljs
        // script request, then deep-link to /validation. After WASM boots,
        // the page must not be replaced by the error boundary; un-highlighted
        // code blocks are acceptable, the boundary is not.
        await Page.RouteAsync("**/highlight.min.js", async route => await route.AbortAsync());

        await Page.GotoAsync("/validation");

        // Wait for either the page heading OR the boundary's error heading,
        // whichever appears first. On Server the boundary trips on the
        // initial render's awaited OnRenderedAsync (the WS roundtrip carries
        // the JSException straight back); on Wasm the heading paints first
        // and the boundary only fires after the gate's 5s safety timeout
        // drains the queue. The explicit wait below covers both timings.
        await Page.Locator("main h1.h2, .rask-error-boundary h1").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30_000 });

        // Give the WASM gate's 5-second safety timeout enough head-room to
        // fire AND for the resulting JSException to round-trip through
        // OnRenderedAsync into RootErrorBoundary. Without this wait the
        // Wasm host snapshots the page during the brief un-highlighted
        // window before the gate drains and incorrectly reports "no
        // boundary", missing the regression.
        await Page.WaitForTimeoutAsync(7_000);

        var boundary = await Page.Locator(".rask-error-boundary h1:has-text(\"Something went wrong\")").CountAsync();
        if (boundary > 0)
        {
            var errorText = await Page.Locator(".rask-error-boundary pre").First.InnerTextAsync();
            Assert.Fail(
                "RootErrorBoundary tripped because the gate let `Rask.CodeSample.rendered` " +
                "run even though hljs failed to load — `window.hljs` is undefined so " +
                "`highlightElement` throws a JSException. A failed-to-load Head asset " +
                "should leave the page un-highlighted, not crash it. " +
                $"Boundary error message: {errorText}");
        }

        // Sanity check that we actually reached the /validation route (not
        // a misroute that never had any CodeSample to begin with).
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 5_000 });
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
