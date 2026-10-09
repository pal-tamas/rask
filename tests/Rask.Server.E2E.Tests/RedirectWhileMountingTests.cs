using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A menu link to a page that decides, as it mounts, that the reader belongs elsewhere — "no partner chosen,
///     go to the partner list" — in a real browser.
/// </summary>
/// <remarks>
///     The reader lands on the list, with its address and its title, without the document reloading and without
///     ever being shown the page that sent them on. That page takes no place in the history: Back from the list
///     is the page the link was clicked on, exactly as after the <c>302</c> a first request for it is answered with.
/// </remarks>
public sealed class RedirectWhileMountingTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_link_to_a_page_that_redirects_as_it_mounts_lands_on_the_destination_and_shows_nothing_of_it()
    {
        await using var session = await HookSession.OpenAsync<TripApp>(playwright, beforeLoad: WatchAsync, path: "/trip");
        var page = session.Page;
        await Expect(page.Locator("#start")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => { window.__stayed = true; }");

        await page.ClickAsync("#to-work");

        await Expect(page.Locator("#list")).ToHaveTextAsync("choose a partner");
        await Expect(page).ToHaveURLAsync(session.BaseUrl + "/trip/partners");
        await Expect(page).ToHaveTitleAsync("Partners | Trip");
        Assert.True(await page.EvaluateAsync<bool>("() => window.__stayed === true"), "the navigation reloaded the document");
        Assert.DoesNotContain("work", await page.EvaluateAsync<string[]>("() => window.__seen"));
        Assert.DoesNotContain("Work | Trip", await page.EvaluateAsync<string[]>("() => window.__titles"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Back_from_the_destination_is_the_page_the_link_was_clicked_on_and_forward_is_the_destination()
    {
        await using var session = await HookSession.OpenAsync<TripApp>(playwright, path: "/trip");
        var page = session.Page;
        await page.ClickAsync("#to-work");
        await Expect(page.Locator("#list")).ToBeVisibleAsync();

        await page.GoBackAsync();
        await Expect(page.Locator("#start")).ToBeVisibleAsync();
        var back = page.Url;
        await page.GoForwardAsync();

        await Expect(page.Locator("#list")).ToBeVisibleAsync();
        Assert.Equal(session.BaseUrl + "/trip", back);
        Assert.Equal(session.BaseUrl + "/trip/partners", page.Url);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Opening_the_redirecting_page_by_its_address_lands_on_the_destination_too()
    {
        await using var session = await HookSession.OpenAsync<TripApp>(playwright, path: "/trip/work");
        var page = session.Page;

        await Expect(page.Locator("#list")).ToHaveTextAsync("choose a partner");

        Assert.Equal(session.BaseUrl + "/trip/partners", page.Url);
        Assert.Equal("Partners | Trip", await page.TitleAsync());
    }

    [Fact]
    public async Task A_link_to_a_page_that_loads_and_then_redirects_lands_on_the_destination_and_back_skips_it()
    {
        await using var session = await HookSession.OpenAsync<TripApp>(playwright, path: "/trip");
        var page = session.Page;
        await Expect(page.Locator("#start")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => { window.__stayed = true; }");

        await page.ClickAsync("#to-record");
        await Expect(page.Locator("#list")).ToHaveTextAsync("choose a partner");
        var landed = page.Url;
        await page.GoBackAsync();

        await Expect(page.Locator("#start")).ToBeVisibleAsync();
        Assert.Equal(session.BaseUrl + "/trip/partners", landed);
        Assert.Equal(session.BaseUrl + "/trip", page.Url);
        Assert.True(await page.EvaluateAsync<bool>("() => window.__stayed === true"), "the navigation reloaded the document");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Signing_in_with_a_return_address_whose_page_redirects_as_it_mounts_lands_on_the_destination()
    {
        await using var session = await HookSession.OpenAsync<TripApp>(
            playwright, beforeLoad: WatchAsync, path: "/trip-login", cookieSignIn: true);
        var page = session.Page;

        await page.ClickAsync("#sign-in");

        await Expect(page.Locator("#vault-list")).ToHaveTextAsync("partners for alice");
        await Expect(page).ToHaveURLAsync(session.BaseUrl + "/trip-vault/partners");
        Assert.DoesNotContain("vault-work", await page.EvaluateAsync<string[]>("() => window.__seen"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task The_page_a_sign_in_redirect_leads_to_is_guarded_for_a_reader_who_has_not_signed_in()
    {
        await using var session = await HookSession.OpenAsync<TripApp>(
            playwright, path: "/trip-vault/partners", cookieSignIn: true);
        var page = session.Page;

        var shown = await page.Locator("#vault-list").CountAsync();

        Assert.Equal(0, shown);
        Assert.Contains("/Account/Login", page.Url, StringComparison.Ordinal);
    }

    // Which page's content is in the document, and the tab's title, noted after every change to either.
    private static Task WatchAsync(IPage page) =>
        page.AddInitScriptAsync(
            """
            window.__seen = [];
            window.__titles = [];
            new MutationObserver(() => {
                for (const id of ['start', 'list', 'work', 'vault-work', 'vault-list']) {
                    if (document.getElementById(id)) window.__seen.push(id);
                }
                window.__titles.push(document.title);
            }).observe(document, { subtree: true, childList: true, characterData: true });
            """);
}

/// <summary>A menu, a start page, a partner list, and a page that needs a partner and has none.</summary>
public sealed partial class TripApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/trip")]
public sealed partial class TripLayout(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } t ? $"{t} | Trip" : "Trip"];

    protected override Component? Render() =>
    [
        Nav[
            NavLink.Href(Routes.TripWorkPage()).Id("to-work")["Work"],
            NavLink.Href(Routes.TripPartnersPage()).Id("to-partners")["Partners"],
            NavLink.Href(Routes.TripRecordPage(404)).Id("to-record")["Record 404"]
        ],
        Main[Outlet],
    ];
}

[Route("")]
[ParentRoute(typeof(TripLayout))]
public sealed partial class TripStartPage : Component
{
    protected override string? PageTitle => "Start";

    protected override Component? Render() => P.Id("start")["start"];
}

[Route("partners")]
[ParentRoute(typeof(TripLayout))]
public sealed partial class TripPartnersPage : Component
{
    protected override string? PageTitle => "Partners";

    protected override Component? Render() => P.Id("list")["choose a partner"];
}

// No partner is ever chosen here, so the page always sends the reader to the list.
[Route("work")]
[ParentRoute(typeof(TripLayout))]
public sealed partial class TripWorkPage : Component
{
    protected override string? PageTitle => "Work";

    protected override Task OnMount()
    {
        Routes.TripPartnersPage().Go();
        return Task.CompletedTask;
    }

    protected override Component? Render() => P.Id("work")["work for nobody"];
}

// Loads its record, finds none, and sends the reader back to the list.
[Route("record/{Id}")]
[ParentRoute(typeof(TripLayout))]
public sealed partial class TripRecordPage : Component
{
    private string? _name;

    [RouteParam] public int Id { get; set; }

    protected override async Task OnMount()
    {
        _name = await Find(Id);
        if (_name is null)
        {
            Routes.TripPartnersPage().Go();
        }
    }

    protected override Component? Render() => P.Id("record")[_name ?? "loading"];

    private static async Task<string?> Find(int id)
    {
        await Task.Delay(20).ConfigureAwait(false);
        return id == 1 ? "First" : null;
    }
}

/// <summary>Signs the reader in and sends them back to the page they came for.</summary>
[Route("/trip-login")]
[AllowAnonymous]
public sealed partial class TripLoginPage(AuthSignIn auth) : Component
{
    protected override Component? Render() => Button.Id("sign-in").OnClick(SignIn)["Sign in"];

    private Task SignIn()
    {
        var alice = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "alice"), new Claim(ClaimTypes.NameIdentifier, "alice")],
            LiveServerHost.CookieScheme);
        return auth.SignIn(new ClaimsPrincipal(alice), Routes.TripVaultWorkPage(), LiveServerHost.CookieScheme);
    }
}

[Route("/trip-vault")]
[Authorize]
public sealed partial class TripVaultLayout : Component
{
    protected override Component? Render() => Main[Outlet];
}

// Where a signed-in reader comes back to; with no partner chosen yet, it sends them to choose one.
[Route("work")]
[ParentRoute(typeof(TripVaultLayout))]
public sealed partial class TripVaultWorkPage : Component
{
    protected override Task OnMount()
    {
        Routes.TripVaultPartnersPage().Go();
        return Task.CompletedTask;
    }

    protected override Component? Render() => P.Id("vault-work")["work for nobody"];
}

[Route("partners")]
[ParentRoute(typeof(TripVaultLayout))]
public sealed partial class TripVaultPartnersPage(IUserProvider users) : Component
{
    protected override Component? Render() => P.Id("vault-list")[$"partners for {users.Current.Identity?.Name}"];
}
