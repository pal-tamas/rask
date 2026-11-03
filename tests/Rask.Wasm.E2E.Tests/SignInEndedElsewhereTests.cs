using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Wasm.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Wasm.E2E.Tests;

/// <summary>
///     Two tabs of one browser on a WASM app. A tab reads its user once, at boot; when the browser's credential is gone
///     — signed out in the other tab, or a cookie that lapsed — it goes to the sign-in page with the way back at its
///     next action, as a Server tab does, with no message.
/// </summary>
/// <remarks>
///     The tab asks as it becomes the reader's again (rask-wasm-auth.ts), and hears Rask's own API refuse it with a
///     401 (SignInRefusals). Only a browser proves the halves meet: that the question reaches the user provider and
///     carries the cookie as it is, and that what the reader pressed meanwhile runs nothing for the user before.
/// </remarks>
public sealed class SignInEndedElsewhereTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // The first WASM boot downloads and starts the runtime.
    private static readonly LocatorAssertionsToHaveTextOptions Boot = new() { Timeout = 60_000 };
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 15_000 };
    private static readonly PageAssertionsToHaveURLOptions Arrived = new() { Timeout = 15_000 };

    [Fact]
    public async Task A_tab_signed_out_in_another_tab_goes_to_the_sign_in_page_with_the_way_back_at_its_next_action()
    {
        await using var host = await SignInHost.StartAsync();
        await using var context = await playwright.Browser.NewContextAsync();
        var desk = await OpenDeskAsync(context, host);

        var other = await context.NewPageAsync();
        await other.GotoAsync(host.BaseUrl + "/desk");
        await Expect(other.Locator("#who")).ToHaveTextAsync("who=ana", Boot);
        await other.ClickAsync("#sign-out");
        await Expect(other.Locator("#who")).ToHaveTextAsync("who=nobody", Settled);
        await ComeBackToAsync(desk);
        await PressIfStillThereAsync(desk);

        await Expect(desk.Locator("#sign-in-page")).ToHaveTextAsync("sign in here", Settled);
        await Expect(desk).ToHaveURLAsync(host.BaseUrl + "/login?returnUrl=%2Fdesk", Arrived);
        Assert.DoesNotContain(host.Calls, call => call.StartsWith("/api/note", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tab_whose_call_to_its_own_api_is_refused_goes_to_the_sign_in_page_with_the_way_back()
    {
        await using var host = await SignInHost.StartAsync();
        await using var context = await playwright.Browser.NewContextAsync();
        var desk = await OpenDeskAsync(context, host);

        // The cookie lapses: nothing in the page hears of it until its next request.
        await context.ClearCookiesAsync();
        await desk.ClickAsync("#act");

        await Expect(desk.Locator("#sign-in-page")).ToHaveTextAsync("sign in here", Settled);
        await Expect(desk).ToHaveURLAsync(host.BaseUrl + "/login?returnUrl=%2Fdesk", Arrived);
        Assert.Contains("/api/note:401", host.Calls);
    }

    private static async Task<IPage> OpenDeskAsync(IBrowserContext context, SignInHost host)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/token?user=ana");
        await Expect(page.Locator("#who")).ToHaveTextAsync("who=ana", Boot);
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
