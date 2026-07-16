using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// The JWT-on-Server host's single journey. The token lives in ProtectedSessionStorage (no URL
// token, no JS-readable token); login sets the principal in-session and the Authorize component
// gates the members content over the live connection. Covers the admin round trip (incl. the
// at-rest token being encrypted, not a raw JWT) then a non-admin who sees no admin note.
[Collection(JwtServerAuthExampleCollection.Name)]
public sealed class JwtServerAuthExampleTests : IAsyncLifetime
{
    private readonly JwtServerAuthAppFixture _app;
    private readonly PlaywrightFixture _pw;
    private IBrowserContext _ctx = default!;
    private IPage _page = default!;

    public JwtServerAuthExampleTests(JwtServerAuthAppFixture app, PlaywrightFixture pw)
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
    public async Task Journey_JwtLogin_AdminRoundTrip_ThenNonAdmin()
    {
        // 1. Anonymous members page → the component gate shows the sign-in prompt.
        await _page.GotoAsync("/members");
        await Expect(_page.Locator("#members-anon"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // 2. Sign in as admin.
        await _page.GotoAsync("/login");
        await Expect(_page.Locator("#login-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await _page.Locator("#username").FillAsync("root");
        await _page.Locator("#password").FillAsync("password");
        await _page.Locator("#login-submit").ClickAsync();

        // 3. Lands on /members, authenticated, with the admin note.
        await Expect(_page).ToHaveURLAsync(new Regex(@"/members$"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
        await Expect(_page.Locator("#members-greeting"))
            .ToContainTextAsync("root", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Expect(_page.Locator("#admin-note"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 4. The raw JWT must NOT be readable in JS — sessionStorage holds only the encrypted blob.
        //    A raw JWT *starts with* the base64url "eyJ" header, so that's what we check. Don't reach for
        //    DoesNotContain here: the value is a Data Protection ciphertext, and "eyJ" turning up somewhere
        //    inside base64url random bytes is pure chance (~1 run in 900) — that assertion failed exactly
        //    that way, on ciphertext that was never a JWT.
        var stored = await _page.EvaluateAsync<string?>("() => sessionStorage.getItem('rask.jwt')");
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.False(
            stored!.StartsWith("eyJ", StringComparison.Ordinal),
            $"sessionStorage must hold the encrypted blob, not a raw JWT — got: {stored}");

        // 5. Sign out → back to /login.
        await _page.Locator("#logout").ClickAsync();
        await Expect(_page).ToHaveURLAsync(new Regex(@"/login"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });

        // 6. Non-admin sign-in: authenticates but sees no admin note.
        await Expect(_page.Locator("#login-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await _page.Locator("#username").FillAsync("alice");
        await _page.Locator("#password").FillAsync("password");
        await _page.Locator("#login-submit").ClickAsync();
        await Expect(_page.Locator("#members-greeting"))
            .ToContainTextAsync("alice", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Expect(_page.Locator("#admin-note")).ToHaveCountAsync(0);
    }
}
