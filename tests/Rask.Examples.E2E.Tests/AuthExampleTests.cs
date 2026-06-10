using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Full visual cookie-login round trip against the minimal Rask.Example.Auth host:
// protected page → challenge redirect → sign-in handshake → land on the protected page → sign out.
[Collection(AuthExampleCollection.Name)]
public sealed class AuthExampleTests : IAsyncLifetime
{
    private readonly AuthExampleAppFixture _app;
    private readonly PlaywrightFixture _pw;
    private IBrowserContext _ctx = default!;
    private IPage _page = default!;

    public AuthExampleTests(AuthExampleAppFixture app, PlaywrightFixture pw)
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
    public async Task CookieLogin_RoundTrip_ChallengeSignInLandSignOut()
    {
        // 1. Anonymous deep-link to a [Authorize] page → cookie challenge → lands on /login with ReturnUrl.
        await _page.GotoAsync("/members");
        await Expect(_page).ToHaveURLAsync(new Regex(@"/login\?ReturnUrl=.*members"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
        await Expect(_page.Locator("#login-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // 2. Sign in as the admin user. The form submit drives the redeem handshake + reconnect.
        await _page.Locator("#username").FillAsync("root");
        await _page.Locator("#password").FillAsync("password");
        await _page.Locator("#login-submit").ClickAsync();

        // 3. Handshake completes → returnUrl lands us back on the protected page, now authenticated.
        await Expect(_page).ToHaveURLAsync(new Regex(@"/members$"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
        await Expect(_page.Locator("#members-greeting"))
            .ToContainTextAsync("root", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(_page.Locator("#admin-note"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 }); // role gate

        // 4. Sign out → handshake clears the cookie → back to /login.
        await _page.Locator("#logout").ClickAsync();
        await Expect(_page).ToHaveURLAsync(new Regex(@"/login"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });

        // 5. The protected page is gated again now that we're signed out.
        await _page.GotoAsync("/members");
        await Expect(_page).ToHaveURLAsync(new Regex(@"/login\?ReturnUrl=.*members"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
    }

    [Fact]
    public async Task NonAdmin_DoesNotSeeAdminNote()
    {
        await _page.GotoAsync("/login?ReturnUrl=%2Fmembers");
        await Expect(_page.Locator("#login-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await _page.Locator("#username").FillAsync("alice");
        await _page.Locator("#password").FillAsync("password");
        await _page.Locator("#login-submit").ClickAsync();

        await Expect(_page.Locator("#members-greeting"))
            .ToContainTextAsync("alice", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Expect(_page.Locator("#admin-note")).ToHaveCountAsync(0); // alice is not an admin
    }
}
