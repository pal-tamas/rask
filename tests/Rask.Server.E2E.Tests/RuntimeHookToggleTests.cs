using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A popover toggled from an element that is not a button, and <c>aria-expanded</c> kept true to every
///     popover, in a real browser.
/// </summary>
/// <remarks>
///     Measured on Flux UI's live tooltip page on 2026-10-07, on its <c>toggleable</c> tooltip (a
///     <c>popover="manual"</c>): hovering for 700 ms does nothing; a click opens it and the pointer leaving
///     does not close it; a second click closes it; Enter and Space toggle it; Escape, a press outside and Tab
///     away close it; a click inside the bubble leaves it open. Its trigger carries <c>aria-expanded</c>,
///     "false" at rest and "true" while it shows.
/// </remarks>
public sealed class RuntimeHookToggleTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_click_on_a_toggle_opens_its_popover_and_the_next_closes_it_while_hovering_does_neither()
    {
        await using var session = await HookSession.OpenAsync<ToggleHookPage>(playwright);
        var page = session.Page;

        await page.HoverAsync("#trigger");
        await page.WaitForTimeoutAsync(700);
        var hovered = await session.ShownAsync("#tip");
        await page.ClickAsync("#trigger");
        var clicked = await session.ShownAsync("#tip");
        await page.Mouse.MoveAsync(900, 600);
        var pointerAway = await session.ShownAsync("#tip");
        await page.ClickAsync("#trigger");

        Assert.False(hovered);
        Assert.True(clicked);
        Assert.True(pointerAway);
        Assert.False(await session.ShownAsync("#tip"));
    }

    [Fact]
    public async Task Enter_and_Space_on_a_focused_toggle_do_what_a_click_does_and_Space_does_not_scroll_the_page()
    {
        await using var session = await HookSession.OpenAsync<ToggleHookPage>(playwright);
        var page = session.Page;

        await page.FocusAsync("#trigger");
        await page.Keyboard.PressAsync("Enter");
        var afterEnter = await session.ShownAsync("#tip");
        await page.Keyboard.PressAsync("Enter");
        var afterSecondEnter = await session.ShownAsync("#tip");
        await page.Keyboard.PressAsync("Space");

        Assert.True(afterEnter);
        Assert.False(afterSecondEnter);
        Assert.True(await session.ShownAsync("#tip"));
        Assert.Equal(0, await page.EvaluateAsync<double>("() => scrollY"));
    }

    [Fact]
    public async Task Escape_a_press_outside_and_Tab_away_close_a_manual_popover_and_a_press_inside_it_does_not()
    {
        await using var session = await HookSession.OpenAsync<ToggleHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#trigger");
        await page.ClickAsync("#inside");
        var afterPressInside = await session.ShownAsync("#tip");
        await page.Keyboard.PressAsync("Escape");
        var afterEscape = await session.ShownAsync("#tip");
        await page.ClickAsync("#trigger");
        await page.Mouse.ClickAsync(900, 600);
        var afterPressOutside = await session.ShownAsync("#tip");
        await page.ClickAsync("#trigger");
        await page.FocusAsync("#trigger");
        await page.Keyboard.PressAsync("Shift+Tab");

        Assert.True(afterPressInside);
        Assert.False(afterEscape);
        Assert.False(afterPressOutside);
        Assert.False(await session.ShownAsync("#tip"));
    }

    [Fact]
    public async Task A_click_on_the_toggle_of_an_open_auto_popover_closes_it_rather_than_opening_it_again()
    {
        await using var session = await HookSession.OpenAsync<ToggleHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#auto-trigger");
        var opened = await session.ShownAsync("#auto");
        await page.ClickAsync("#auto-trigger");

        Assert.True(opened);
        Assert.False(await session.ShownAsync("#auto"));
    }

    [Fact]
    public async Task A_press_in_a_field_inside_a_toggle_puts_the_caret_there_and_one_beside_the_field_toggles()
    {
        await using var session = await HookSession.OpenAsync<ToggleHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#typed-part");
        var afterField = await session.ShownAsync("#auto");
        var focused = await page.EvaluateAsync<string>("() => document.activeElement.id");
        await page.ClickAsync("#typed-room");

        // Flux's typed date trigger: the fields are inside what opens the calendar, and do not open it.
        Assert.False(afterField);
        Assert.Equal("typed-part", focused);
        Assert.True(await session.ShownAsync("#auto"));
    }

    [Fact]
    public async Task Every_invoker_that_carries_aria_expanded_follows_its_popover_and_one_that_does_not_never_gains_it()
    {
        await using var session = await HookSession.OpenAsync<ToggleHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#native");
        await Expect(page.Locator("#native")).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(page.Locator("#command")).ToHaveAttributeAsync("aria-expanded", "true");
        var bareWhileOpen = await page.GetAttributeAsync("#bare", "aria-expanded");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#native")).ToHaveAttributeAsync("aria-expanded", "false");
        await page.ClickAsync("#trigger");

        Assert.Null(bareWhileOpen);
        await Expect(page.Locator("#trigger")).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(page.Locator("#command")).ToHaveAttributeAsync("aria-expanded", "false");
    }
}

/// <summary>Popovers and the elements that open them, written out by hand.</summary>
public sealed partial class ToggleHookPage : Component
{
    private const string Html = """
        <button id="before" type="button">before</button>
        <span id="trigger" role="button" tabindex="0" aria-expanded="false" data-rask-toggle="tip">?</span>
        <div id="tip" popover="manual" style="inset:auto;top:100px;left:100px">Help <button id="inside" type="button">more</button></div>
        <span id="auto-trigger" tabindex="0" aria-expanded="false" data-rask-toggle="auto">auto</span>
        <div id="auto" popover style="inset:auto;top:200px;left:100px">Auto</div>
        <div id="typed" data-rask-toggle="auto" style="display:flex;width:300px"><input id="typed-part" size="2"><span id="typed-room" style="flex:1;height:20px"></span></div>
        <button id="native" type="button" popovertarget="panel" aria-expanded="false">native</button>
        <button id="bare" type="button" popovertarget="panel">bare</button>
        <button id="command" type="button" commandfor="panel" command="toggle-popover" aria-expanded="false">command</button>
        <div id="panel" popover style="inset:auto;top:300px;left:100px">Panel</div>
        <div style="height:3000px"></div>
        """;

    protected override Component? HeadAssets => Markup.Title["toggle hooks"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[Raw.Value(Html)];
}
