using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// JWT + WASM: the bearer token lives in localStorage and is sent as Authorization: Bearer on every API call;
// /api/me validates it server-side. The Authorize component gates the members content.
[Collection(WasmJwtAuthExampleCollection.Name)]
public sealed class WasmJwtAuthExampleTests : IAsyncLifetime
{
    private readonly WasmJwtAuthAppFixture _app;
    private readonly PlaywrightFixture _pw;
    private IBrowserContext _ctx = default!;
    private IPage _page = default!;

    public WasmJwtAuthExampleTests(WasmJwtAuthAppFixture app, PlaywrightFixture pw)
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
    public async Task Login_RoundTrip_TokenInLocalStorage_AdminSeesAdminNote_ThenSignOut()
    {
        await _page.GotoAsync("/members");
        await Expect(_page.Locator("#members-anon"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 90_000 });

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

        // The bearer JWT is in localStorage (this sample's choice).
        var stored = await _page.EvaluateAsync<string?>("() => localStorage.getItem('rask.jwt')");
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.StartsWith("eyJ", stored); // a raw JWT begins with the base64url "eyJ" header

        await _page.Locator("#logout").ClickAsync();
        await Expect(_page).ToHaveURLAsync(new Regex(@"/login"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
        var cleared = await _page.EvaluateAsync<string?>("() => localStorage.getItem('rask.jwt')");
        Assert.True(string.IsNullOrEmpty(cleared));
    }

    [Fact]
    public async Task NonAdmin_DoesNotSeeAdminNote()
    {
        await _page.GotoAsync("/login");
        await Expect(_page.Locator("#login-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 90_000 });
        await _page.Locator("#username").FillAsync("alice");
        await _page.Locator("#password").FillAsync("password");
        await _page.Locator("#login-submit").ClickAsync();

        await Expect(_page.Locator("#members-greeting"))
            .ToContainTextAsync("alice", new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });
        await Expect(_page.Locator("#admin-note")).ToHaveCountAsync(0);
    }
}
