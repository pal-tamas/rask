using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// The Cookie + WASM host's single journey: a browser-WASM SPA signs in against its host's
// /api/login (HttpOnly cookie) and hydrates the user from /api/me; the Authorize component gates
// the members content off that principal. Admin round trip then a non-admin who sees no admin note.
[Collection(WasmCookieAuthExampleCollection.Name)]
public sealed class WasmCookieAuthExampleTests : IAsyncLifetime
{
    private readonly WasmCookieAuthAppFixture _app;
    private readonly PlaywrightFixture _pw;
    private IBrowserContext _ctx = default!;
    private IPage _page = default!;

    public WasmCookieAuthExampleTests(WasmCookieAuthAppFixture app, PlaywrightFixture pw)
    {
        _app = app;
        _pw = pw;
    }

    public async Task InitializeAsync()
    {
        _ctx = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl });
        _page = await _ctx.NewPageAsync();
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task Journey_CookieWasmLogin_AdminRoundTrip_ThenNonAdmin()
    {
        // 1. Anonymous → /api/me 204 → the gate shows the sign-in prompt (WASM cold boot can be slow).
        await _page.GotoAsync("/members");
        await Expect(_page.Locator("#members-anon"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 90_000 });

        // 2. Sign in as admin — /api/login sets the cookie, RefreshAsync re-hydrates from /api/me.
        await _page.GotoAsync("/login");
        await Expect(_page.Locator("#login-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 60_000 });
        await _page.Locator("#username").FillAsync("root");
        await _page.Locator("#password").FillAsync("password");
        await _page.Locator("#login-submit").ClickAsync();

        await Expect(_page).ToHaveURLAsync(new Regex(@"/members$"),
            new PageAssertionsToHaveURLOptions { Timeout = 60_000 });
        await Expect(_page.Locator("#members-greeting"))
            .ToContainTextAsync("root", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Expect(_page.Locator("#admin-note"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 3. Sign out → back to /login.
        await _page.Locator("#logout").ClickAsync();
        await Expect(_page).ToHaveURLAsync(new Regex(@"/login"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });

        // 4. Non-admin sign-in: authenticates but sees no admin note.
        await Expect(_page.Locator("#login-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 60_000 });
        await _page.Locator("#username").FillAsync("alice");
        await _page.Locator("#password").FillAsync("password");
        await _page.Locator("#login-submit").ClickAsync();
        await Expect(_page.Locator("#members-greeting"))
            .ToContainTextAsync("alice", new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });
        await Expect(_page.Locator("#admin-note")).ToHaveCountAsync(0);
    }
}
