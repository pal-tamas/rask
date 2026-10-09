using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The hooks an overlay needs — where focus goes around a popover, a dialog a render opens, how a modal is
///     dismissed, <c>data-open</c>, and the page lock — in a real browser.
/// </summary>
/// <remarks>
///     Measured on Flux UI's live pages on 2026-10-07: its modal carries <c>open</c> and <c>data-open</c> while
///     shown and fires <c>cancel</c> then <c>close</c> for Escape AND for a press outside; under an open select,
///     dropdown and popover <c>&lt;html&gt;</c> has <c>overflow: hidden; pointer-events: none; scrollbar-gutter:
///     stable</c>, and under a modal the same without <c>pointer-events</c>; a press outside a menu left focus
///     on the button that opens it. The gutter is kept only where a scrollbar was taking room, which the
///     browser here — scrollbars hidden, as in every headless run — never has: <c>RuntimeHookLockTests</c>.
/// </remarks>
public sealed class RuntimeHookOverlayTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Lock = "() => { const s = getComputedStyle(document.documentElement); return s.overflow + '|' + s.pointerEvents + '|' + s.scrollbarGutter; }";

    [Fact]
    public async Task Tabbing_out_of_an_open_popover_closes_it_and_shift_tab_back_to_its_button_does_not()
    {
        await using var session = await HookSession.OpenAsync<OverlayHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#pop-open");
        await page.FocusAsync("#pop-first");
        await page.Keyboard.PressAsync("Shift+Tab");
        var backOnInvoker = await session.ShownAsync("#pop");
        await page.FocusAsync("#pop-last");
        await page.Keyboard.PressAsync("Tab");

        Assert.True(backOnInvoker, "going back to the button that opens it closed the popover");
        Assert.False(await session.ShownAsync("#pop"));
        Assert.Equal("after", await session.FocusAsync());
    }

    [Fact]
    public async Task A_popover_closed_by_a_press_outside_hands_focus_to_the_button_that_opens_it()
    {
        await using var session = await HookSession.OpenAsync<OverlayHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#pop-open");
        await page.FocusAsync("#pop-first");
        await page.Mouse.ClickAsync(900, 650);

        await Expect(page.Locator("#pop")).ToBeHiddenAsync();
        await Expect(page.Locator("#pop-open")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task A_manual_popover_is_left_open_when_focus_leaves_it()
    {
        await using var session = await HookSession.OpenAsync<OverlayHookPage>(playwright);
        var page = session.Page;

        await page.EvaluateAsync("() => document.getElementById('manual').showPopover()");
        await page.FocusAsync("#manual-button");
        await page.Keyboard.PressAsync("Tab");

        Assert.True(await session.ShownAsync("#manual"));
    }

    [Fact]
    public async Task A_dialog_the_page_says_is_open_is_a_real_modal_and_closes_when_the_page_stops_saying_so()
    {
        await using var session = await HookSession.OpenAsync<OverlayHookPage>(playwright);
        var page = session.Page;
        var dialog = page.Locator("#state");

        await page.ClickAsync("#state-open");
        await Expect(dialog).ToBeVisibleAsync();
        var modal = await dialog.EvaluateAsync<bool>("d => d.matches(':modal')");
        var marked = await dialog.EvaluateAsync<bool>("d => d.hasAttribute('data-open')");
        var backdrop = await dialog.EvaluateAsync<string>("d => getComputedStyle(d, '::backdrop').display");
        await page.ClickAsync("#state-close");

        Assert.True(modal, "the dialog was shown, but not in the top layer");
        Assert.True(marked);
        Assert.Equal("block", backdrop);
        await Expect(dialog).ToBeHiddenAsync();
        Assert.False(await dialog.EvaluateAsync<bool>("d => d.hasAttribute('data-open')"));
    }

    [Fact]
    public async Task Escape_on_a_state_driven_modal_reaches_the_pages_own_close_handler()
    {
        await using var session = await HookSession.OpenAsync<OverlayHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#state-open");
        await Expect(page.Locator("#state")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");

        await Expect(page.Locator("#closed")).ToHaveTextAsync("closed=1");
        await Expect(page.Locator("#state")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task A_modal_dismissed_by_press_closes_on_a_press_outside_and_stays_through_Escape()
    {
        await using var session = await HookSession.OpenAsync<OverlayHookPage>(playwright);
        var page = session.Page;
        var dialog = page.Locator("#press");

        await dialog.EvaluateAsync("d => { window.__events = []; for (const n of ['cancel', 'close']) d.addEventListener(n, () => window.__events.push(n)); d.showModal(); }");
        await page.Keyboard.PressAsync("Escape");
        await page.Keyboard.PressAsync("Escape");
        var afterEscape = await dialog.EvaluateAsync<bool>("d => d.open");
        await page.Mouse.ClickAsync(10, 10);

        Assert.True(afterEscape, "Escape closed a modal that only a press outside closes");
        await Expect(dialog).ToBeHiddenAsync();
        await page.WaitForFunctionAsync("() => window.__events.length === 2");
        Assert.Equal(["cancel", "close"], await page.EvaluateAsync<string[]>("() => window.__events"));
    }

    [Fact]
    public async Task A_modal_dismissed_by_escape_stays_through_a_press_outside_and_one_inside_never_closes_it()
    {
        await using var session = await HookSession.OpenAsync<OverlayHookPage>(playwright);
        var page = session.Page;
        var dialog = page.Locator("#escape");

        await dialog.EvaluateAsync("d => d.showModal()");
        var marked = await dialog.EvaluateAsync<bool>("d => d.hasAttribute('data-open')");
        await page.Mouse.ClickAsync(10, 10);
        await page.ClickAsync("#escape-inside");
        var afterPresses = await dialog.EvaluateAsync<bool>("d => d.open");
        await page.Keyboard.PressAsync("Escape");

        Assert.True(marked, "a dialog the browser opened did not get data-open");
        Assert.True(afterPresses);
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.Locator("#escape[data-open]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task An_invoker_command_opens_and_closes_a_modal_in_an_engine_without_invoker_commands()
    {
        await using var session = await HookSession.OpenAsync<OverlayHookPage>(playwright);
        var page = session.Page;

        // Take the feature away, as an older engine has it: the attributes stay, the behaviour does not.
        await page.EvaluateAsync("() => { delete HTMLButtonElement.prototype.commandForElement; for (const b of document.querySelectorAll('[commandfor]')) { const c = b.cloneNode(true); c.removeAttribute('command'); c.setAttribute('data-command', b.getAttribute('command')); b.replaceWith(c); } for (const b of document.querySelectorAll('[data-command]')) { Object.defineProperty(b, 'getAttribute', { value: function (n) { return n === 'command' ? Element.prototype.getAttribute.call(this, 'data-command') : Element.prototype.getAttribute.call(this, n); } }); } }");
        await page.ClickAsync("#command-open");
        var modal = await page.Locator("#command").EvaluateAsync<bool>("d => d.matches(':modal')");
        await page.ClickAsync("#command-close");

        Assert.True(modal);
        await Expect(page.Locator("#command")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task An_open_overlay_that_asks_holds_the_page_still_and_lets_go_when_it_closes()
    {
        await using var session = await HookSession.OpenAsync<OverlayHookPage>(playwright);
        var page = session.Page;

        var before = await page.EvaluateAsync<string>(Lock);
        await page.ClickAsync("#lock-open");
        var open = await page.EvaluateAsync<string>(Lock);
        var panel = await page.EvaluateAsync<string>("() => getComputedStyle(document.getElementById('lock')).pointerEvents");
        await page.ClickAsync("#lock-inside");
        var pressedInside = await page.EvaluateAsync<string>("() => document.getElementById('lock-inside').dataset.pressed || ''");
        await page.Keyboard.PressAsync("Escape");

        Assert.Equal("visible|auto|auto", before);
        // No gutter: this browser shows no scrollbar to keep one for (RuntimeHookLockTests drives one that does).
        Assert.Equal("hidden|none|auto", open);
        Assert.Equal("auto", panel);
        Assert.Equal("yes", pressedInside);
        Assert.Equal("visible|auto|auto", await page.EvaluateAsync<string>(Lock));
    }

    [Fact]
    public async Task A_dialog_shown_as_a_popover_holds_the_page_still_as_any_popover_does()
    {
        await using var session = await HookSession.OpenAsync<OverlayHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#lock-dialog-open");
        var open = await page.EvaluateAsync<string>(Lock);
        var asDialog = await page.EvaluateAsync<bool>("() => document.getElementById('lock-dialog').open");
        await page.Keyboard.PressAsync("Escape");

        // A date picker's popup: the element is a <dialog>, the browser shows it as a popover, and `open` stays false.
        Assert.Equal("hidden|none|auto", open);
        Assert.False(asDialog);
        Assert.Equal("visible|auto|auto", await page.EvaluateAsync<string>(Lock));
    }

    [Fact]
    public async Task Two_locking_overlays_are_counted_and_one_removed_while_open_lets_go_too()
    {
        await using var session = await HookSession.OpenAsync<OverlayHookPage>(playwright);
        var page = session.Page;

        await page.EvaluateAsync("() => { document.getElementById('lock-scroll').showModal(); document.getElementById('lock').showPopover(); }");
        await page.WaitForTimeoutAsync(50);
        var both = await page.EvaluateAsync<string>(Lock);
        await page.EvaluateAsync("() => document.getElementById('lock').hidePopover()");
        await page.WaitForTimeoutAsync(50);
        var modalOnly = await page.EvaluateAsync<string>(Lock);
        await page.EvaluateAsync("() => document.getElementById('lock-scroll').remove()");
        await page.WaitForTimeoutAsync(50);

        Assert.Equal("hidden|none|auto", both);
        // Flux's modal: the page does not scroll, and keeps its pointer (the dialog makes it inert anyway).
        Assert.Equal("hidden|auto|auto", modalOnly);
        Assert.Equal("visible|auto|auto", await page.EvaluateAsync<string>(Lock));
    }
}

/// <summary>The attributes of the overlay hooks, written out by hand, around one dialog the page's state opens.</summary>
public sealed partial class OverlayHookPage : Component
{
    private const string Html = """
        <style>body { margin: 0; height: 3000px } dialog { margin: auto; width: 200px; height: 100px }</style>
        <button id="pop-open" type="button" popovertarget="pop">Open</button>
        <div id="pop" popover style="position:fixed;left:20px;top:60px;margin:0">
          <button id="pop-first" type="button">First</button>
          <button id="pop-last" type="button">Last</button>
        </div>
        <button id="after" type="button">After</button>
        <div id="manual" popover="manual" style="position:fixed;left:300px;top:60px;margin:0"><button id="manual-button" type="button">In</button></div>
        <dialog id="press" data-rask-modal="press"><button type="button">Inside</button></dialog>
        <dialog id="escape" data-rask-modal="escape"><button id="escape-inside" type="button">Inside</button></dialog>
        <button id="command-open" type="button" command="show-modal" commandfor="command">Open</button>
        <dialog id="command" data-rask-modal><button id="command-close" type="button" command="close" commandfor="command">Close</button></dialog>
        <button id="lock-open" type="button" popovertarget="lock">Lock</button>
        <div id="lock" popover data-rask-lock style="position:fixed;left:20px;top:200px;margin:0">
          <button id="lock-inside" type="button" onclick="this.dataset.pressed='yes'">Inside</button>
        </div>
        <dialog id="lock-scroll" data-rask-lock="scroll">Modal</dialog>
        <button id="lock-dialog-open" type="button" popovertarget="lock-dialog">Lock</button>
        <dialog id="lock-dialog" popover data-rask-lock style="position:fixed;left:20px;top:300px;margin:0">Calendar</dialog>
        """;

    private bool _open;
    private int _closed;

    protected override Component? HeadAssets => Markup.Title["overlay hooks"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("closed")[$"closed={_closed}"],
        Button.Id("state-open").OnClick(() => _open = true)["open"],
        Dialog.Id("state").Data("rask-modal-open", _open ? "true" : "false").OnClose(() => { _open = false; _closed++; })[
            Button.Id("state-close").OnClick(() => _open = false)["close"]
        ],
        Div[Raw.Value(Html)]
    ];
}
