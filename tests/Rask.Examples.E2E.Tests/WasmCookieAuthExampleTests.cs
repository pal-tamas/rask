using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Cookie + WASM: a browser-WASM SPA signs in against its host's /api/login (HttpOnly cookie) and hydrates
// the user from /api/me. The Authorize component gates the members content off that principal.
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
    public async Task Login_RoundTrip_AdminSeesAdminNote_ThenSignOut()
    {
        // Anonymous → /api/me 204 → the gate shows the sign-in prompt (WASM cold boot can be slow).
        await _page.GotoAsync("/members");
        await Expect(_page.Locator("#members-anon")).ToBeVisibleAsync(new() { Timeout = 90_000 });

        await _page.GotoAsync("/login");
        await Expect(_page.Locator("#login-submit")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await _page.Locator("#username").FillAsync("root");
        await _page.Locator("#password").FillAsync("password");
        await _page.Locator("#login-submit").ClickAsync();

        // /api/login set the cookie, RefreshAsync re-hydrated from /api/me, navigated to /members.
        await Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/members$"),
            new() { Timeout = 60_000 });
        await Expect(_page.Locator("#members-greeting")).ToContainTextAsync("root", new() { Timeout = 30_000 });
        await Expect(_page.Locator("#admin-note")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await _page.Locator("#logout").ClickAsync();
        await Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/login"),
            new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task NonAdmin_DoesNotSeeAdminNote()
    {
        await _page.GotoAsync("/login");
        await Expect(_page.Locator("#login-submit")).ToBeVisibleAsync(new() { Timeout = 90_000 });
        await _page.Locator("#username").FillAsync("alice");
        await _page.Locator("#password").FillAsync("password");
        await _page.Locator("#login-submit").ClickAsync();

        await Expect(_page.Locator("#members-greeting")).ToContainTextAsync("alice", new() { Timeout = 60_000 });
        await Expect(_page.Locator("#admin-note")).ToHaveCountAsync(0);
    }
}
