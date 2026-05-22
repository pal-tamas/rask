using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

public abstract partial class ExampleSmokeTests
{
    [Fact]
    public Task Routing_UserDetail_NonNumericId_BindsRawString() => RunAsync(async () =>
    {
        // RouteParam string Id should accept any value, including non-numeric.
        // The page must render and the bound Id must show "abc" in the body.
        await Page.GotoAsync("/users/abc");
        await Expect(Page.Locator("main h1.h2, main h1.h3, main h1.h4, main h1").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Expect(Page.Locator("main")).ToContainTextAsync("abc",
            new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Routing_UserDetail_UnknownTabValue_DoesNotCrash() => RunAsync(async () =>
    {
        // ?tab=banana — the page must still render. The framework binds
        // whatever string came in; the page is responsible for handling
        // unknown values gracefully.
        await Page.GotoAsync("/users/42?tab=banana");
        await Expect(Page.Locator("main h1.h2, main h1.h3, main h1.h4, main h1").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
    });

    [Fact]
    public Task Routing_LiveTicker_InvalidSymbol_RendersLoadingState() => RunAsync(async () =>
    {
        // Unknown symbol — the LiveTicker still mounts and the page header
        // renders. The synthetic feed falls through to a generic seed price
        // (LiveTicker.SeedPrice) for symbols outside BTC/ETH/SOL, so the
        // ticker simply ticks at a flat baseline — the assertion just confirms
        // the page didn't crash.
        await Page.GotoAsync("/realtime/UNKNOWN");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("UNKNOWN",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
    });
}
