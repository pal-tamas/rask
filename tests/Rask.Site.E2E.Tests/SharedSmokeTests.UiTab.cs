using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

public abstract partial class SharedSmokeTests
{
    /// <summary>
    ///     The UI tab, the way a reader finds it: the bar's "UI" from a docs page, a component in its own sidebar, and
    ///     Back twice to the docs page the walk left — all inside the app, which the journey's sentinel proves.
    /// </summary>
    protected async Task WalkUiTabAsync()
    {
        var docsPage = Page.Url;
        var tab = Page.Locator("header.app-navbar a.ui-tab");
        await Expect(tab).Not.ToHaveAttributeAsync("aria-current", "page");

        await tab.ClickAsync();
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Introduction",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Expect(tab).ToHaveAttributeAsync("aria-current", "page");
        await Expect(Page.Locator(".side-nav")).ToHaveCountAsync(0);

        // The sidebar is Flux's navlist, and the router lights the row of the page being read.
        var sidebar = Page.Locator("[data-ui-sidebar] nav[data-ui-navlist]");
        await sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Button", Exact = true }).ClickAsync();
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Button",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Expect(sidebar.Locator("a[aria-current='page']")).ToHaveTextAsync("Button");
        await Expect(Page.Locator("[data-testid='ui-button-variants'] [data-ui-button]")).ToHaveCountAsync(6);
        await Expect(Page.Locator("[data-testid='ui-api'] [data-ui-table]").First).ToBeVisibleAsync();
        await Expect(tab).ToHaveAttributeAsync("aria-current", "page");
        await AssertNoGlobalCrashAsync();

        await Page.GoBackAsync();
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Introduction",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });

        await Page.GoBackAsync();
        await Expect(Page.Locator(".side-nav")).ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 30_000 });
        Assert.Equal(docsPage, Page.Url);
        await Expect(tab).Not.ToHaveAttributeAsync("aria-current", "page");
        await AssertNoGlobalCrashAsync();
    }
}
