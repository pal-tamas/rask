using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task Navigator_SetMultipleParams_ThenRemoveOne_KeepsOthers() => RunAsync(async () =>
    {
        await NavigateToAsync("/navigator");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Navigator",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("button:has-text('SetQuery page=1')").ClickAsync();
        await Page.Locator("button:has-text('SetQuery sort=asc')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]sort=asc"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });

        await Page.Locator("button:has-text('RemoveQuery page')").ClickAsync();
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]page="),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]sort=asc"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Navigator_ClearQuery_EmptiesUrlQueryString() => RunAsync(async () =>
    {
        await NavigateToAsync("/navigator");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Navigator",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("button:has-text('SetQuery page=1')").ClickAsync();
        await Page.Locator("button:has-text('SetQuery sort=asc')").ClickAsync();
        await Page.Locator("button:has-text('ClearQuery')").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/navigator$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Navigator_NavigateWithQuery_ReplacesQueryEntirely() => RunAsync(async () =>
    {
        await NavigateToAsync("/navigator");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Navigator",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Set some seed query params first.
        await Page.Locator("button:has-text('SetQuery page=1')").ClickAsync();
        await Page.Locator("button:has-text('SetQuery sort=asc')").ClickAsync();

        // Navigate(path, query) — should drop sort/page, keep only from=button.
        await Page.Locator("button:has-text('Navigate(path, query)')").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]from=button"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]sort="),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]page="),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
    });
}
