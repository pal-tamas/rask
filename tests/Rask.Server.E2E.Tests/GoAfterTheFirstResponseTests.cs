using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     "Signed in already? Then not the sign-in page", opened by its address, in a real browser: the page checks the
///     store as it mounts, and the check outlasts the first response.
/// </summary>
/// <remarks>
///     The page is shown while it checks. When the check sends the reader on — to a page of the app, or out of it —
///     the address is replaced, not pushed: Back does not return to a page that would send them on again. Over the
///     socket and over the HTTP stream alike.
/// </remarks>
public sealed class GoAfterTheFirstResponseTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // Far shorter than the check: the first response is out before the page decides.
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(50);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_page_that_goes_to_another_once_its_check_ends_lands_there_in_place_of_itself(bool overHttp)
    {
        var check = new RecheckGate();
        await using var session = await Open(check, "/recheck/to", overHttp);
        var page = session.Page;
        await Expect(page.Locator("#checking")).ToHaveTextAsync("checking");
        var entries = await page.EvaluateAsync<int>("() => history.length");

        check.Ends(signedIn: true);

        await Expect(page.Locator("#home")).ToHaveTextAsync("home");
        Assert.Equal(session.BaseUrl + "/recheck/home", page.Url);
        Assert.Equal(entries, await page.EvaluateAsync<int>("() => history.length"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_page_that_goes_out_once_its_check_ends_loads_that_page_in_place_of_itself(bool overHttp)
    {
        var check = new RecheckGate();
        await using var session = await Open(check, "/recheck/out", overHttp);
        var page = session.Page;
        await Expect(page.Locator("#checking")).ToHaveTextAsync("checking");
        var entries = await page.EvaluateAsync<int>("() => history.length");

        check.Ends(signedIn: true);

        await Expect(page.Locator("#outside")).ToHaveTextAsync("outside the app");
        Assert.Equal(session.BaseUrl + "/outside", page.Url);
        Assert.Equal(entries, await page.EvaluateAsync<int>("() => history.length"));
    }

    // The connection is proven open by a click's round trip before the check ends.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_page_whose_check_ends_after_its_connection_opened_still_goes_on(bool overHttp)
    {
        var check = new RecheckGate();
        await using var session = await Open(check, "/recheck/to", overHttp);
        var page = session.Page;
        await page.ClickAsync("#ping");
        await Expect(page.Locator("#pinged")).ToHaveTextAsync("pinged");

        check.Ends(signedIn: true);

        await Expect(page.Locator("#home")).ToHaveTextAsync("home");
        Assert.Equal(session.BaseUrl + "/recheck/home", page.Url);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_page_whose_check_finds_nobody_signed_in_stays(bool overHttp)
    {
        var check = new RecheckGate();
        await using var session = await Open(check, "/recheck/to", overHttp);
        var page = session.Page;
        await Expect(page.Locator("#checking")).ToHaveTextAsync("checking");

        check.Ends(signedIn: false);

        await Expect(page.Locator("#checking")).ToHaveTextAsync("sign in");
        Assert.Equal(session.BaseUrl + "/recheck/to", page.Url);
        Assert.Empty(session.PageErrors);
    }

    private Task<HookSession> Open(RecheckGate check, string path, bool overHttp) =>
        HookSession.OpenAsync<RecheckApp>(
            playwright, path: path, blockWebSockets: overHttp,
            server: o => o.QuiescenceTimeout = Brief,
            services: s => s.AddSingleton(check),
            beside: app => app.MapGet(
                "/outside",
                () => Results.Content(
                    "<!DOCTYPE html><html><head><title>Outside</title></head><body><p id=\"outside\">outside the app</p></body></html>",
                    "text/html")));
}

/// <summary>The store the sign-in page asks; it answers when the test says.</summary>
public sealed class RecheckGate
{
    private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<bool> SignedIn => _answer.Task;

    public void Ends(bool signedIn) => _answer.TrySetResult(signedIn);
}

public sealed partial class RecheckApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/recheck/to")]
public sealed partial class RecheckToPage(RecheckGate check) : Component
{
    private string _state = "checking";
    private bool _pinged;

    protected override async Task OnMount()
    {
        if (await check.SignedIn)
        {
            Routes.RecheckHomePage().Go();
            return;
        }

        _state = "sign in";
    }

    protected override Component? Render() =>
    [
        P.Id("checking")[_state],
        Button.Id("ping").OnClick(() => _pinged = true)["ping"],
        P.Id("pinged")[_pinged ? "pinged" : ""],
    ];
}

[Route("/recheck/out")]
public sealed partial class RecheckOutPage(RecheckGate check) : Component
{
    protected override async Task OnMount()
    {
        if (await check.SignedIn)
        {
            Go.Out("/outside");
        }
    }

    protected override Component? Render() => P.Id("checking")["checking"];
}

[Route("/recheck/home")]
public sealed partial class RecheckHomePage : Component
{
    protected override Component? Render() => P.Id("home")["home"];
}
