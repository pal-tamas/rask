using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// The Wasm.Host journey: the WASM bundle served by an ASP.NET host with a SPA fallback, so
// deep-link + refresh and slow-3G apply. There is no server WebSocket to drop, so the
// offline→reconnect step is off.
[Collection(WasmExampleCollection.Name)]
public sealed class WasmExampleTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Journey_WalksEveryPageAndUnusualActivity() => RunAsync(() =>
        RunShowcaseJourneyAsync(new ShowcaseJourneyOptions
        {
            DeepLink = true,
            OfflineReconnect = false,
            Slow3g = true,
        }));

    // The WASM-only PWA example page (PwaDemo) lives in the WASM host and is surfaced in the shared
    // sidebar via a host-contributed ShowcaseNavEntry. Verify the entry routes and the page renders.
    [Fact]
    public Task PwaExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator("aside.side-nav button.nav-item-btn").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("PWA demo");
        await Expect(Page.Locator("main h1.h2")).ToContainTextAsync("PWA — notifications & push",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#pwa-notify")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#pwa-push")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    });
}
