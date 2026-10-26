using System.Security.Claims;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>[Guest(Then = "/gg/home")]</c> on a sign-in page, in a real browser: a reader already signed in never sees
///     it — opened by its address or by a link, they land on Then, and Back does not return to the sign-in page.
/// </summary>
public sealed class GuestPageTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_reader_who_is_not_signed_in_sees_the_guest_page()
    {
        await using var session = await HookSession.OpenAsync<GuestApp>(playwright, path: "/gg/login", cookieSignIn: true);
        var page = session.Page;

        await Expect(page.Locator("#login")).ToHaveTextAsync("sign in here");

        Assert.Equal(session.BaseUrl + "/gg/login", page.Url);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task A_signed_in_reader_who_follows_a_link_to_the_guest_page_lands_on_its_then_in_place_of_it()
    {
        await using var session = await HookSession.OpenAsync<GuestApp>(
            playwright, beforeLoad: WatchAsync, path: "/gg/start", cookieSignIn: true);
        var page = session.Page;
        await SignInAsync(page);
        var entries = await page.EvaluateAsync<int>("() => history.length");

        await page.ClickAsync("#to-login");

        await Expect(page.Locator("#home")).ToHaveTextAsync("home for alice");
        Assert.Equal(session.BaseUrl + "/gg/home", page.Url);
        Assert.Equal(entries, await page.EvaluateAsync<int>("() => history.length"));
        Assert.DoesNotContain("login", await page.EvaluateAsync<string[]>("() => window.__seen"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task A_signed_in_reader_who_opens_the_guest_page_by_its_address_is_redirected_to_its_then()
    {
        await using var session = await HookSession.OpenAsync<GuestApp>(playwright, path: "/gg/start", cookieSignIn: true);
        var page = session.Page;
        await SignInAsync(page);

        var response = await page.GotoAsync(session.BaseUrl + "/gg/login");

        await Expect(page.Locator("#home")).ToHaveTextAsync("home for alice");
        Assert.Equal(session.BaseUrl + "/gg/home", page.Url);
        Assert.Equal(session.BaseUrl + "/gg/login", (await response!.Request.RedirectedFrom!.ResponseAsync())!.Url);
    }

    internal static async Task SignInAsync(Microsoft.Playwright.IPage page)
    {
        await page.ClickAsync("#sign-in");
        await Expect(page.Locator("#start")).ToHaveTextAsync("start for alice");
    }

    // Which page's content has been in the document, noted after every change.
    private static Task WatchAsync(Microsoft.Playwright.IPage page) =>
        page.AddInitScriptAsync(
            """
            window.__seen = [];
            new MutationObserver(() => {
                for (const id of ['start', 'login', 'home']) {
                    if (document.getElementById(id)) window.__seen.push(id);
                }
            }).observe(document, { subtree: true, childList: true, characterData: true });
            """);
}

// The path base is one value for the whole process, so the suites that map an app under one take turns.
[Collection(PathBaseCollection.Name)]
public sealed class GuestPageUnderAPathBaseTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_signed_in_reader_under_a_path_base_lands_on_the_apps_own_page()
    {
        await using var session = await HookSession.OpenAsync<GuestApp>(
            playwright, path: "/uj/gg/start", cookieSignIn: true, pathBase: "/uj");
        var page = session.Page;
        await GuestPageTests.SignInAsync(page);

        await page.GotoAsync(session.BaseUrl + "/uj/gg/login");

        await Expect(page.Locator("#home")).ToHaveTextAsync("home for alice");
        Assert.Equal(session.BaseUrl + "/uj/gg/home", page.Url);
    }
}

public sealed partial class GuestApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/gg/start")]
public sealed partial class GuestStartPage(AuthSignIn auth, IUserProvider users) : Component
{
    protected override Component? Render() =>
    [
        P.Id("start")[$"start for {users.Current.Identity?.Name}"],
        Button.Id("sign-in").OnClick(SignIn)["Sign in"],
        NavLink.Href(Routes.GuestLoginPage()).Id("to-login")["Sign in page"],
    ];

    private Task SignIn()
    {
        var alice = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "alice"), new Claim(ClaimTypes.NameIdentifier, "alice")],
            LiveServerHost.CookieScheme);
        return auth.SignIn(new ClaimsPrincipal(alice), Routes.GuestStartPage(), LiveServerHost.CookieScheme);
    }
}

[Route("/gg/login")]
[Guest(Then = "/gg/home")]
public sealed partial class GuestLoginPage : Component
{
    protected override Component? Render() => P.Id("login")["sign in here"];
}

[Route("/gg/home")]
public sealed partial class GuestHomePage(IUserProvider users) : Component
{
    protected override Component? Render() => P.Id("home")[$"home for {users.Current.Identity?.Name}"];
}
