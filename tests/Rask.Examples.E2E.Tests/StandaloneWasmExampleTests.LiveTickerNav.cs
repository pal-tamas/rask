using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Regression test for the LiveTicker sidebar-nav URL bug.
//
// User report: clicking the "Live ticker" entry in the sidebar (which calls
// `nav.Navigate("/realtime/BTC")`) routes to the new page but the browser URL
// stays at /index.html. The page renders correctly; the address bar lies.
//
// Root cause was in WasmLiveSession.BuildPayloadCoalescingRerendersAsync:
// the first build emitted the navigation payload with a `history.url` field,
// but a rerender landing mid-dispatch (e.g. LiveTicker's poll-loop
// StateHasChanged firing right after mount) caused the loop to rebuild — and
// the rebuild dropped `historyUrl`, producing a history-less final payload.
// The client received the new HTML but no pushHistory call, so
// location.pathname stayed pinned.
//
// This test exists in StandaloneWasmExampleTests (not the shared inherited
// suite) because the bug is specific to WasmLiveSession's coalescing path —
// Server uses a single-pass RenderAndSendAsync with no rebuild loop, and
// Wasm.Host serves the same WASM bundle as standalone so it would observe
// the same bug too (separate test would be redundant).
public sealed partial class StandaloneWasmExampleTests
{
    [Fact]
    public async Task LiveTickerSidebarNav_UpdatesBrowserUrl()
    {
        try
        {
            await NavigateToAsync("/");
            await Expect(Page.Locator("h1.display-5"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

            // Click the sidebar entry — its OnClick calls nav.Navigate("/realtime/BTC").
            await ClickSidebar("Live ticker");

            // The page itself must route (h2 "BTC live ticker" appears) AND the URL
            // must end at /realtime/BTC. Pre-fix, the page rendered correctly but
            // the URL stayed at /index.html because the publish-render rebuild
            // dropped the history entry.
            await Expect(Page.Locator("main h1.h2"))
                .ToHaveTextAsync("BTC live ticker",
                    new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
            await Expect(Page)
                .ToHaveURLAsync(new Regex(".*/realtime/BTC$"),
                    new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        }
        finally
        {
            await TestArtifacts.DumpAsync(
                Page, FixtureName, nameof(LiveTickerSidebarNav_UpdatesBrowserUrl), ServerLog);
        }
    }
}
