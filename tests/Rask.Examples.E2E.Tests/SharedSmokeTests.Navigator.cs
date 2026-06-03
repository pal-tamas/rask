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
    public Task Navigator_RouteParamChange_UpdatesTitleViaHeadDiff() => RunAsync(async () =>
    {
        // Head-diff path: navigating between /users/{id} values changes the <title> AND only
        // body text (a supported diff), so the server ships a diff carrying the new <head> as
        // a fragment that the client morphs into document.head — NOT a full-document morph.
        // If the client head morph were missing, the title would freeze at the prior value.
        await NavigateToAsync("/users/42");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("User #42",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Expect(Page).ToHaveTitleAsync("User #42 — Rask",
            new PageAssertionsToHaveTitleOptions { Timeout = 5_000 });

        // In-page SPA navigation (Navigator.Navigate, client-intercepted — no full reload):
        // same page type, route param changes from 42 → 137.
        await Page.Locator("button:has-text('#137')").ClickAsync();

        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("User #137",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
        await Expect(Page).ToHaveTitleAsync("User #137 — Rask",
            new PageAssertionsToHaveTitleOptions { Timeout = 5_000 });
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/users/137$"),
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
