using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a test app; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Two tabs of one browser. The browser's credential changes in one — through a plain endpoint of the host that
///     Rask knows nothing about, or through Rask's own sign-in — and the other, whose socket was opened with the old
///     one, acts under the new one at its next action.
/// </summary>
/// <remarks>
///     A request per update gets this from the cookie it carries. A socket sees the cookie once, so the tab asks when
///     it becomes the reader's again and opens its connection anew before it sends (rask-auth-watch.ts). Only a
///     browser proves the halves meet: that the question carries the cookie, that the upgrade after it does, and
///     that what the reader typed and pressed meanwhile is neither lost nor run for the user before.
/// </remarks>
public sealed class CredentialChangedElsewhereTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 15_000 };

    [Fact]
    public async Task A_tab_acts_in_the_tenant_its_user_chose_in_another_tab_and_keeps_what_was_typed_in_it()
    {
        await using var host = await StartAsync();
        await using var context = await playwright.Browser.NewContextAsync();
        var desk = await OpenDeskAsync(context, host, "/token?user=alice&tenant=7");
        await desk.FillAsync("#note", "half a thought");

        var elsewhere = await context.NewPageAsync();
        await elsewhere.GotoAsync(host.BaseUrl + "/token?user=alice&tenant=8");
        await ComeBackToAsync(desk);
        await desk.ClickAsync("#act");

        await Expect(desk.Locator("#acted")).ToHaveTextAsync("acted=alice:8", Settled);
        await Expect(desk.Locator("#who")).ToHaveTextAsync("who=alice:8");
        await Expect(desk.Locator("#mounts")).ToHaveTextAsync("mounts=1");
        await Expect(desk.Locator("#note")).ToHaveValueAsync("half a thought");
        Assert.Equal(["alice:8"], WhoDeskPage.Acted);
    }

    [Fact]
    public async Task A_tab_whose_browser_now_carries_another_user_loads_again_as_them_and_runs_nothing_for_the_one_before()
    {
        await using var host = await StartAsync();
        await using var context = await playwright.Browser.NewContextAsync();
        var desk = await OpenDeskAsync(context, host, "/token?user=alice&tenant=7");

        var elsewhere = await context.NewPageAsync();
        await elsewhere.GotoAsync(host.BaseUrl + "/token?user=bob&tenant=7");
        await ComeBackToAsync(desk);
        await PressIfStillThereAsync(desk);

        // The press is held while the tab asks and is dropped with alice's page: nothing ran for her, and what
        // the tab shows is bob's.
        await Expect(desk.Locator("#who")).ToHaveTextAsync("who=bob:7", Settled);
        Assert.DoesNotContain("alice:7", WhoDeskPage.Acted);
    }

    [Fact]
    public async Task A_tab_whose_browser_no_longer_carries_anybody_goes_to_the_sign_in_page_with_the_way_back()
    {
        await using var host = await StartAsync();
        await using var context = await playwright.Browser.NewContextAsync();
        var desk = await OpenDeskAsync(context, host, "/token?user=alice&tenant=7");

        var elsewhere = await context.NewPageAsync();
        await elsewhere.GotoAsync(host.BaseUrl + "/gone");
        await ComeBackToAsync(desk);
        await PressIfStillThereAsync(desk);

        await Expect(desk.Locator("#sign-in-page")).ToHaveTextAsync("sign in here", Settled);
        await Expect(desk).ToHaveURLAsync(host.BaseUrl + "/login?returnUrl=%2Fwhose");
        Assert.Empty(WhoDeskPage.Acted);
    }

    // Rask's own sign-in changes the cookie through its handoff, and the tab that did it tells the others.
    [Fact]
    public async Task A_tab_open_beside_the_one_that_signed_in_acts_as_the_signed_in_user_without_being_touched_first()
    {
        await using var host = await StartAsync();
        await using var context = await playwright.Browser.NewContextAsync();
        var desk = await OpenDeskAsync(context, host, "/whose");
        await desk.FillAsync("#note", "half a thought");

        var other = await context.NewPageAsync();
        await other.GotoAsync(host.BaseUrl + "/whose");
        await other.ClickAsync("#sign-in");
        await Expect(other.Locator("#who")).ToHaveTextAsync("who=carol:3", Settled);

        await Expect(desk.Locator("#who")).ToHaveTextAsync("who=carol:3", Settled);
        await desk.ClickAsync("#act");
        await Expect(desk.Locator("#acted")).ToHaveTextAsync("acted=carol:3", Settled);
        await Expect(desk.Locator("#mounts")).ToHaveTextAsync("mounts=1");
        await Expect(desk.Locator("#note")).ToHaveValueAsync("half a thought");
    }

    // A loaded host can be slow to answer the question. Late or never is no answer, and never "changed": the tab keeps
    // its socket, what the reader pressed leaves after a moment, and the question gives up without a reconnect.
    [Fact]
    public async Task A_tab_whose_question_is_never_answered_keeps_its_socket_and_acts_after_a_moment()
    {
        await using var host = await StartAsync();
        await using var context = await playwright.Browser.NewContextAsync();
        var desk = await OpenDeskAsync(context, host, "/token?user=alice&tenant=7");
        var sockets = 0;
        desk.WebSocket += (_, _) => Interlocked.Increment(ref sockets);
        var gaveUp = new TaskCompletionSource();
        desk.RequestFailed += (_, request) =>
        {
            if (request.Url.Contains("/_rask/auth/who", StringComparison.Ordinal))
            {
                gaveUp.TrySetResult();
            }
        };
        // Held, never answered: the request stays open until the page gives up on it.
        await desk.RouteAsync("**/_rask/auth/who**", static _ => Task.CompletedTask);

        await ComeBackToAsync(desk);
        await desk.ClickAsync("#act");
        await Expect(desk.Locator("#acted")).ToHaveTextAsync("acted=alice:7", Settled);
        await gaveUp.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await desk.ClickAsync("#act");

        // The second press renders what the first did, so its arrival is read off the server, bounded.
        for (var waited = 0; WhoDeskPage.Acted.Count < 2 && waited < 150; waited++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Equal(["alice:7", "alice:7"], WhoDeskPage.Acted);
        Assert.Equal(0, Volatile.Read(ref sockets));
        await Expect(desk.Locator("#mounts")).ToHaveTextAsync("mounts=1");
    }

    private static Task<LiveServerHost> StartAsync()
    {
        WhoDeskPage.Acted.Clear();
        return LiveServerHost.StartAsync<WhoApp>(blockWebSockets: false, cookieSignIn: true, beside: IssuesTokens);
    }

    // The app's own endpoints, as an older API in the same host has them: one issues the cookie — again, with
    // another tenant or for another user — and one takes it away. Neither goes through Rask.
    private static void IssuesTokens(WebApplication app)
    {
        app.MapGet("/token", async context =>
        {
            await context.SignInAsync(
                LiveServerHost.CookieScheme,
                WhoDeskPage.Principal(context.Request.Query["user"].ToString(), context.Request.Query["tenant"].ToString()));
            context.Response.Redirect("/whose");
        });
        app.MapGet("/gone", async context =>
        {
            await context.SignOutAsync(LiveServerHost.CookieScheme);
            await context.Response.WriteAsync("gone");
        });
    }

    private static async Task<IPage> OpenDeskAsync(IBrowserContext context, LiveServerHost host, string path)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + path);
        await Expect(page.Locator("#mounts")).ToHaveTextAsync("mounts=1", Settled);

        // Connected before anything changes elsewhere: the socket this journey is about is the one opened now.
        await page.WaitForFunctionAsync("() => document.body.inert === false && !document.querySelector('.rask-overlay[data-show]')");
        return page;
    }

    // The reader presses the button at once, as a hand already on the mouse does. The page it is on is about to be
    // replaced, and a press that waits for the button to settle would wait for one that has gone.
    private static Task PressIfStillThereAsync(IPage page) =>
        page.EvaluateAsync("() => document.getElementById('act')?.click()");

    // The reader comes back to the tab. A headless page never loses the focus it never had to take, so the event a
    // real window raises is raised here.
    private static async Task ComeBackToAsync(IPage page)
    {
        await page.BringToFrontAsync();
        await page.EvaluateAsync("() => window.dispatchEvent(new Event('focus'))");
    }
}

public sealed partial class WhoApp : Component
{
    protected override Component? HeadAssets => Title["who"];
    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

/// <summary>Shows who the page is for, who its last handler ran for, and how often it was mounted.</summary>
[Route("/whose")]
[AllowAnonymous]
public sealed partial class WhoDeskPage(IUserProvider users, AuthSignIn auth) : Component
{
    /// <summary>Who each handler ran for, across every tab of the test in progress.</summary>
    public static ConcurrentQueue<string> Acted { get; } = new();

    private string _acted = "none";
    private int _mounts;

    public static ClaimsPrincipal Principal(string user, string tenant) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user), new Claim("tenant", tenant)],
            LiveServerHost.CookieScheme));

    protected override Task OnMount()
    {
        _mounts++;
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        P.Id("who")[$"who={Who()}"],
        P.Id("acted")[$"acted={_acted}"],
        P.Id("mounts")[$"mounts={_mounts}"],
        Input.Value(string.Empty).Id("note"),
        Button.Id("act").OnClick(Act)["act"],
        Button.Id("sign-in").OnClick(() => auth.SignIn(Principal("carol", "3"), "/whose", LiveServerHost.CookieScheme))["sign in"]
    ];

    private void Act()
    {
        _acted = Who();
        Acted.Enqueue(_acted);
    }

    private string Who() =>
        users.Current.Identity is { IsAuthenticated: true, Name: var name }
            ? $"{name}:{users.Current.FindFirst("tenant")?.Value}"
            : "nobody";
}

[Route("/login")]
[AllowAnonymous]
public sealed partial class WhoLoginPage : Component
{
    protected override Component? Render() => P.Id("sign-in-page")["sign in here"];
}
