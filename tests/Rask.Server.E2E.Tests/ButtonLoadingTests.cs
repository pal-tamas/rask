using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a one-element test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A button waiting on its own server handler says so, in a real browser, and is not pressed twice.
/// </summary>
/// <remarks>
///     <c>LoadingStateTests</c> proves the module's rules in node over a fake clock. What only a browser proves is
///     the wiring: that the Server runtime begins a ticket for the press it sends, ends it on that seq's ack, and
///     that the render the handler causes does not strip the mark while the ack is still on its way.
/// </remarks>
public sealed class ButtonLoadingTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 15_000 };

    [Fact]
    public async Task A_slow_handler_marks_its_button_until_it_is_done_and_drops_a_second_press()
    {
        await using var host = await LiveServerHost.StartAsync<SlowSavePage>(blockWebSockets: false);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=0", Settled);

        var save = page.Locator("#save");
        await save.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        // Past the 200 ms delay and still inside the 1.2 s handler: marked, busy, and still focused.
        await Expect(save).ToHaveAttributeAsync("data-loading", "", new() { Timeout = 5_000 });
        await Expect(save).ToHaveAttributeAsync("aria-busy", "true");
        Assert.True(await save.EvaluateAsync<bool>("el => document.activeElement === el"), "the press threw focus off the button");

        // A second press by keyboard while it visibly waits is the double submit this exists to stop.
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=1", Settled);
        await Expect(save).Not.ToHaveAttributeAsync("data-loading", "", new() { Timeout = 5_000 });
        await Expect(save).Not.ToHaveAttributeAsync("aria-busy", "true");

        // Give a dropped press every chance to have been sent anyway before asserting it was not.
        await page.WaitForTimeoutAsync(1_500);
        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=1");
    }

    [Fact]
    public async Task A_fast_handler_never_shows_the_mark()
    {
        await using var host = await LiveServerHost.StartAsync<SlowSavePage>(blockWebSockets: false);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        await Expect(page.Locator("#bumps")).ToHaveTextAsync("bumps=0", Settled);

        // Recorded by an observer rather than polled, so a mark that came and went between two polls still counts.
        await page.EvaluateAsync("""
            () => {
                window.__marked = false;
                new MutationObserver(() => {
                    if (document.querySelector('#bump[data-loading]')) window.__marked = true;
                }).observe(document.body, { attributes: true, subtree: true, childList: true });
            }
            """);

        await page.ClickAsync("#bump");
        await page.ClickAsync("#bump");
        await Expect(page.Locator("#bumps")).ToHaveTextAsync("bumps=2", Settled);
        await page.WaitForTimeoutAsync(400);

        Assert.False(await page.EvaluateAsync<bool>("() => window.__marked"), "a fast handler flashed the loading mark");
    }

    [Fact]
    public async Task An_opted_out_button_is_never_marked_and_every_press_goes_through()
    {
        await using var host = await LiveServerHost.StartAsync<SlowSavePage>(blockWebSockets: false);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        await Expect(page.Locator("#steps")).ToHaveTextAsync("steps=0", Settled);

        var step = page.Locator("#step");
        await step.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(400);

        await Expect(step).Not.ToHaveAttributeAsync("data-loading", "");
        await page.Keyboard.PressAsync("Enter");

        // Presses on a stepper are meant to queue: both handlers run.
        await Expect(page.Locator("#steps")).ToHaveTextAsync("steps=2", Settled);
    }
}

/// <summary>A save that takes a while, a counter that does not, and a stepper that opts out.</summary>
public sealed partial class SlowSavePage : Component
{
    private int _saves;
    private int _bumps;
    private int _steps;

    protected override Component? HeadAssets => new Title()["loading"];
    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("saves")[$"saves={_saves}"],
        Button.Id("save").OnClick(async () =>
        {
            await Task.Delay(1_200);
            _saves++;
        })["save"],
        P.Id("bumps")[$"bumps={_bumps}"],
        Button.Id("bump").OnClick(() => _bumps++)["bump"],
        P.Id("steps")[$"steps={_steps}"],
        Button.Id("step").Data("rask-loading", "off").OnClick(async () =>
        {
            await Task.Delay(600);
            _steps++;
        })["step"]
    ];
}
