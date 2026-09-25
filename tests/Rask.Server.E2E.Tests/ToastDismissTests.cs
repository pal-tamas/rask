using Rask.Core;
using Rask.Core.Components;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A toast that dismisses itself, in a real browser, and the page state that goes with it.
/// </summary>
/// <remarks>
///     <para>
///     <c>UiToastTests</c> proves the markup: the dismiss control the runtime looks for and the attribute that
///     says how long to wait. What only a browser proves is the part that matters — that the timer fires, that
///     it dismisses by CLICKING that control so the page's own handler runs and the page removes the toast from
///     its state, and that the countdown pauses while somebody is reading it.
///     </para>
///     <para>
///     The last one is the reason this is not a node fixture. Pausing on a real pointer entering a real element
///     is the behaviour, and a stub DOM would be asserting that the code calls the functions it calls.
///     </para>
/// </remarks>
public sealed class ToastDismissTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_toast_with_a_duration_dismisses_itself_through_the_pages_own_handler()
    {
        await using var host = await LiveServerHost.StartAsync<ToastPage>(blockWebSockets: false);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");

        await page.Locator("#show").ClickAsync();
        await Expect(page.Locator("[role=\"status\"]")).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // It goes on its own, and the page KNOWS it went — the counter is the page's own state, written by the
        // handler the runtime clicked. A toast that merely hid itself would leave this at 0.
        await Expect(page.Locator("#dismissed")).ToHaveTextAsync("dismissed=1", new() { Timeout = 10_000 });
        await Expect(page.Locator("[role=\"status\"]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task The_countdown_pauses_while_somebody_is_reading_it()
    {
        await using var host = await LiveServerHost.StartAsync<ToastPage>(blockWebSockets: false);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");

        await page.Locator("#show").ClickAsync();
        var toast = page.Locator("[role=\"status\"]");
        await Expect(toast).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Hold the pointer over it for well past the duration. A notice that disappears while somebody is
        // reading it, or mid-way through reaching for its action, is worse than one that stays.
        await toast.HoverAsync();
        await page.WaitForTimeoutAsync(2_500);
        await Expect(page.Locator("#dismissed")).ToHaveTextAsync("dismissed=0");

        // Move away and the rest of the countdown runs.
        await page.Mouse.MoveAsync(0, 0);
        await Expect(page.Locator("#dismissed")).ToHaveTextAsync("dismissed=1", new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task A_toast_with_no_duration_stays_until_it_is_acknowledged()
    {
        await using var host = await LiveServerHost.StartAsync<ToastPage>(blockWebSockets: false);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");

        await page.Locator("#show-sticky").ClickAsync();
        await Expect(page.Locator("[role=\"status\"]")).ToBeVisibleAsync(new() { Timeout = 5_000 });

        await page.WaitForTimeoutAsync(2_500);
        await Expect(page.Locator("#dismissed")).ToHaveTextAsync("dismissed=0");

        // The button is still the way out.
        await page.Locator("[data-rask-dismiss]").ClickAsync();
        await Expect(page.Locator("#dismissed")).ToHaveTextAsync("dismissed=1", new() { Timeout = 5_000 });
    }
}

/// <summary>A page that shows a notice and counts the ones it has taken back off its own list.</summary>
/// <remarks>
///     Plain elements rather than <c>UiToast</c>, deliberately: the hook is a CORE one — any element carrying
///     <c>data-rask-dismiss-after</c> is dismissed by clicking its own <c>[data-rask-dismiss]</c> control — and
///     testing it through the kit would prove the kit rather than the runtime, as well as pulling Rask.Ui into a
///     runtime suite that has no other reason to reference it.
/// </remarks>
public sealed partial class ToastPage : Component
{
    private bool _up;
    private bool _sticky;
    private int _dismissed;

    protected override Component? HeadAssets => Markup.Title["toast"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("dismissed")[$"dismissed={_dismissed}"],
        Button.Id("show").OnClick(() => { _up = true; _sticky = false; })["show"],
        Button.Id("show-sticky").OnClick(() => { _up = true; _sticky = true; })["show sticky"],
        _up
            ? Notice()
            : null
    ];

    private Component Notice()
    {
        var notice = Div.Role("status").Class("notice");
        if (!_sticky)
        {
            notice = notice.Data("rask-dismiss-after", "1000");
        }

        return notice[
            Span["Saved"],
            Button
                .Attributes(("data-rask-dismiss", null))
                .OnClick(() => { _up = false; _dismissed++; })["Dismiss"]
        ];
    }
}
