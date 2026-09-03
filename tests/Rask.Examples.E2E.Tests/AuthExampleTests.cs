using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// The cookie-login host's single journey against the minimal Rask.Example.Auth host: the full
// admin round trip (protected page → challenge redirect → sign-in handshake → land → role gate →
// sign out → gated again), then a non-admin sign-in that must NOT see the admin-only note.
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

    private static readonly Regex LoginChallengeUrl = new(@"/login\?ReturnUrl=.*members");

    [Fact]
    public async Task Journey_CookieLogin_AdminRoundTrip_ThenNonAdmin()
    {
        // 1. Anonymous deep-link to a [Authorize] page → cookie challenge → /login with ReturnUrl.
        await GotoMembersExpectingChallengeAsync();
        await Expect(_page.Locator("#login-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // 2. Sign in as the admin user — the form submit drives the redeem handshake + reconnect.
        await _page.Locator("#email").FillAsync("ada@example.com");
        await _page.Locator("#password").FillAsync("Password1");
        await _page.Locator("#login-submit").ClickAsync();

        // 3. Handshake completes → returnUrl lands us back on the protected page, authenticated,
        //    with the role-gated admin note.
        await Expect(_page).ToHaveURLAsync(new Regex(@"/members$"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
        await Expect(_page.Locator("#members-greeting"))
            .ToContainTextAsync("ada@example.com", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(_page.Locator("#admin-note"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 4. Sign out → handshake clears the cookie → back to /login, and the page is gated again.
        await _page.Locator("#logout").ClickAsync();
        await Expect(_page).ToHaveURLAsync(new Regex(@"/login"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
        // The sign-out cookie deletion can land a beat after the redirect settles, so a single
        // re-navigation occasionally races it and [Authorize] still serves /members (the content
        // gate then shows its NotAuthorized slot instead of a 302). Re-issue the deep-link until
        // the server challenge actually fires.
        await GotoMembersExpectingChallengeAsync();

        // 5. Non-admin sign-in: authenticates but the admin-only note must be absent (role gating).
        await Expect(_page.Locator("#login-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await _page.Locator("#email").FillAsync("bob@example.com");
        await _page.Locator("#password").FillAsync("Password1");
        await _page.Locator("#login-submit").ClickAsync();
        await Expect(_page.Locator("#members-greeting"))
            .ToContainTextAsync("bob@example.com", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Expect(_page.Locator("#admin-note")).ToHaveCountAsync(0);
    }

    // Deep-link to the [Authorize] page and assert the server cookie challenge redirects to /login.
    // Anonymous deep-links are gated by a server-side 302, but right after sign-out the cookie
    // deletion can commit a beat late, so the first GET may still authenticate and land on /members.
    // Re-issue the navigation a few times until the challenge fires (or fail with the last URL).
    private async Task GotoMembersExpectingChallengeAsync()
    {
        // A few quick retries to absorb a late cookie commit; the last navigation is asserted with
        // Playwright so a genuine regression still fails with its URL/aria-snapshot diagnostics.
        const int retries = 4;
        for (var attempt = 0; attempt < retries; attempt++)
        {
            await _page.GotoAsync("/members");
            if (LoginChallengeUrl.IsMatch(_page.Url))
            {
                return;
            }

            await Task.Delay(500);
        }

        await _page.GotoAsync("/members");
        await Expect(_page).ToHaveURLAsync(LoginChallengeUrl,
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
    }
}
