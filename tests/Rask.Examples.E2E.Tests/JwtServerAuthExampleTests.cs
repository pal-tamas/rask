using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// JWT + Server with the token in ProtectedSessionStorage (no URL token, no JS-readable token). Login sets
// the principal in-session; the Authorize component gates the members content over the live connection.
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
    public async Task Login_RoundTrip_AdminSeesAdminNote_ThenSignOut()
    {
        // Anonymous members page → component gate shows the sign-in prompt.
        await _page.GotoAsync("/members");
        await Expect(_page.Locator("#members-anon")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Sign in as admin.
        await _page.GotoAsync("/login");
        await Expect(_page.Locator("#login-submit")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await _page.Locator("#username").FillAsync("root");
        await _page.Locator("#password").FillAsync("password");
        await _page.Locator("#login-submit").ClickAsync();

        // Lands on /members, authenticated, with the admin note.
        await Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/members$"),
            new() { Timeout = 30_000 });
        await Expect(_page.Locator("#members-greeting")).ToContainTextAsync("root", new() { Timeout = 30_000 });
        await Expect(_page.Locator("#admin-note")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The raw JWT must NOT be readable in JS — sessionStorage holds only the encrypted blob.
        var stored = await _page.EvaluateAsync<string?>("() => sessionStorage.getItem('rask.jwt')");
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.DoesNotContain("eyJ", stored); // not a raw JWT (which starts with the base64 "eyJ" header)

        // Sign out → back to /login.
        await _page.Locator("#logout").ClickAsync();
        await Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/login"),
            new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task NonAdmin_DoesNotSeeAdminNote()
    {
        await _page.GotoAsync("/login");
        await Expect(_page.Locator("#login-submit")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await _page.Locator("#username").FillAsync("alice");
        await _page.Locator("#password").FillAsync("password");
        await _page.Locator("#login-submit").ClickAsync();

        await Expect(_page.Locator("#members-greeting")).ToContainTextAsync("alice", new() { Timeout = 30_000 });
        await Expect(_page.Locator("#admin-note")).ToHaveCountAsync(0);
    }
}
