using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Syntax-highlighting deep diagnostics requiring deep-link / refresh access. Runs on
// Server + Wasm.Host (the SPA-fallback hosts) only. Highlighting is server-side
// (ColorCode token spans in the rendered <code>), so these tests assert the spans are
// present on a fresh deep-link and survive a browser refresh without tripping the
// RootErrorBoundary — no client highlight.js, no head assets, no async settle.
public abstract partial class ExampleSmokeTests
{
    [Fact]
    public Task Highlight_DeepLinkToCodeSamplePage_HighlightsOnFirstPaint() => RunAsync(async () =>
    {
        // Tests every CodeSample page in isolation — direct deep-link, fresh session,
        // no prior in-SPA state. Each page's code blocks must carry ColorCode token
        // spans as soon as the page paints (highlighting is part of the server render).
        var pages = new[]
        {
            ("/validation", "Validation"), ("/routing", "Routing"), ("/navigator", "Navigator"),
            ("/nested-forms", "Complex models"), ("/http", "HttpClient + DI")
        };

        foreach (var (path, heading) in pages)
        {
            await Page.GotoAsync(path);
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync(heading,
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

            await WaitForHighlightedSpansAsync(HighlightSettleTimeoutMs);
            var total = await Page.Locator("pre code[class*='language-']").CountAsync();
            var highlighted = await Page.Locator("pre code[class*='language-']:has(span[class])").CountAsync();
            Assert.True(total > 0, $"{path}: no code blocks found. [{await HighlightDiagnosticsAsync()}]");
            Assert.True(highlighted == total,
                $"{path}: only {highlighted}/{total} blocks highlighted on first paint. " +
                $"[{await HighlightDiagnosticsAsync()}]");
        }
    });

    [Fact]
    public Task Highlight_BrowserRefreshOnCodeSamplePage_DoesNotTripErrorBoundary() => RunAsync(async () =>
    {
        // General refresh smoke: after a successful first paint of /validation, hitting
        // browser refresh ("Reload") must re-render the page (not the RootErrorBoundary's
        // "Something went wrong") and re-emit highlighted code blocks. With highlighting
        // server-side there is no JS hook that could fault on refresh, but this still
        // guards the boot path against regressions.
        await Page.GotoAsync("/validation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await WaitForHighlightedSpansAsync(HighlightSettleTimeoutMs);

        await Page.ReloadAsync();

        var boundary = await Page.Locator(".rask-error-boundary h1:has-text(\"Something went wrong\")").CountAsync();
        Assert.True(boundary == 0, "RootErrorBoundary tripped on refresh of /validation.");

        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await WaitForHighlightedSpansAsync(HighlightSettleTimeoutMs);
        var total = await Page.Locator("pre code[class*='language-']").CountAsync();
        var highlighted = await Page.Locator("pre code[class*='language-']:has(span[class])").CountAsync();
        Assert.True(total > 0,
            $"/validation after refresh: no code blocks found. [{await HighlightDiagnosticsAsync()}]");
        Assert.True(total == highlighted,
            $"/validation after refresh: only {highlighted}/{total} highlighted. " +
            $"[{await HighlightDiagnosticsAsync()}]");
    });
}
