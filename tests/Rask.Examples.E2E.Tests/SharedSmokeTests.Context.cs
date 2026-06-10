using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task Context_ToggleProvider_UpdatesDeepConsumerThroughCachedIntermediate() => RunAsync(async () =>
    {
        // End-to-end exercise of the live diff path for context change-detection: the badge is
        // nested inside ThemeCard (a render-cached, theme-unaware intermediate). Toggling the
        // provider re-renders the demo, and the deep consumer — which bypasses the render cache
        // because it read Context.Required<Theme>() — must update straight through the cached
        // intermediate. If the bypass/walk were wrong, the badge would freeze at "Light".
        await NavigateToAsync("/context");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Context",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var badge = Page.Locator("main .badge");
        await Expect(badge).ToContainTextAsync("Light", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await Page.Locator("button:has-text('Toggle theme')").ClickAsync();
        await Expect(badge).ToContainTextAsync("Dark", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Toggle back to prove it isn't a one-way latch.
        await Page.Locator("button:has-text('Toggle theme')").ClickAsync();
        await Expect(badge).ToContainTextAsync("Light", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });
}
