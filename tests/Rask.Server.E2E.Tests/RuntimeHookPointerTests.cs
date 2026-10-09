using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The hooks a pointer drives — a tooltip, a hover-opening panel, a menu's lit row and the safe area towards
///     its submenu — in a real browser, against what Flux UI's live pages did on 2026-10-07.
/// </summary>
/// <remarks>
///     Flux opens both a tooltip and a <c>flux:dropdown hover</c> in the pointerenter's own task (0–1 ms between
///     the event and the popover's <c>beforetoggle</c>) and closes them in the pointerleave's, so every
///     assertion here is made with no wait at all: a hook that needed a frame would fail it.
/// </remarks>
public sealed class RuntimeHookPointerTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_tooltip_shows_the_moment_the_pointer_arrives_and_hides_the_moment_it_leaves()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;

        await page.HoverAsync("#tip-trigger");
        var shown = await session.ShownAsync("#bubble");
        var expanded = await page.GetAttributeAsync("#tip-trigger", "aria-expanded");
        await page.Mouse.MoveAsync(900, 600);

        Assert.True(shown, "the tooltip was not open in the pointerenter's own task");
        Assert.Equal("true", expanded);
        Assert.False(await session.ShownAsync("#bubble"));
        Assert.Equal("false", await page.GetAttributeAsync("#tip-trigger", "aria-expanded"));
    }

    [Fact]
    public async Task A_tooltip_on_an_element_that_is_no_button_reaches_the_top_layer()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;

        await page.HoverAsync("#plain-trigger");

        // The clipping card around it is 40px tall and the bubble sits below that: only the top layer shows it.
        Assert.True(await session.ShownAsync("#plain-bubble"));
        Assert.True(await page.Locator("#plain-bubble").IsVisibleAsync());
    }

    [Fact]
    public async Task A_press_dismisses_a_tooltip_until_the_pointer_has_left_and_come_back()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;

        await page.HoverAsync("#tip-trigger");
        await page.Mouse.DownAsync();
        var whilePressed = await session.ShownAsync("#bubble");
        await page.Mouse.UpAsync();
        await page.Mouse.MoveAsync(await CenterX(page, "#tip-trigger") + 2, await CenterY(page, "#tip-trigger"));
        var stillOver = await session.ShownAsync("#bubble");
        await page.Mouse.MoveAsync(900, 600);
        await page.HoverAsync("#tip-trigger");

        Assert.False(whilePressed);
        Assert.False(stillOver);
        Assert.True(await session.ShownAsync("#bubble"));
    }

    [Fact]
    public async Task Keyboard_focus_shows_a_tooltip_keeps_it_when_the_pointer_leaves_and_Escape_hides_it()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;

        await session.TabToAsync("#tip-trigger");
        var onFocus = await session.ShownAsync("#bubble");
        await page.HoverAsync("#tip-trigger");
        await page.Mouse.MoveAsync(900, 600);
        var afterPointerLeft = await session.ShownAsync("#bubble");
        await page.Keyboard.PressAsync("Escape");
        var afterEscape = await session.ShownAsync("#bubble");
        await session.TabToAsync("#tip-trigger");
        await page.Keyboard.PressAsync("Tab");

        Assert.True(onFocus);
        Assert.True(afterPointerLeft, "the pointer leaving took the tooltip from a reader who tabbed to it");
        Assert.False(afterEscape);
        Assert.False(await session.ShownAsync("#bubble"));
    }

    [Fact]
    public async Task An_interactive_tooltip_stays_when_focus_drops_to_nothing_until_a_press_outside_and_a_plain_one_closes()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;

        await session.TabToAsync("#tip-trigger");
        await page.EvaluateAsync("() => document.activeElement.blur()");
        var interactive = await session.ShownAsync("#bubble");
        await page.Mouse.ClickAsync(700, 500);
        var afterPressOutside = await session.ShownAsync("#bubble");
        await session.TabToAsync("#plain-trigger");
        var plainShown = await session.ShownAsync("#plain-bubble");
        await page.EvaluateAsync("() => document.activeElement.blur()");

        // Flux UI's interactive tooltip is still open after its trigger's blur(); its plain one is not.
        Assert.True(interactive);
        Assert.False(afterPressOutside);
        Assert.True(plainShown);
        Assert.False(await session.ShownAsync("#plain-bubble"));
    }

    [Fact]
    public async Task Focus_that_a_click_gave_does_not_keep_a_tooltip_when_the_pointer_leaves()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#tip-trigger");
        await page.Mouse.MoveAsync(900, 600);
        await page.HoverAsync("#tip-trigger");
        var returned = await session.ShownAsync("#bubble");
        await page.Mouse.MoveAsync(900, 600);

        // Flux: the trigger still had focus from the click, and the tooltip left with the pointer all the same.
        Assert.Equal("tip-trigger", await session.FocusAsync());
        Assert.True(returned);
        Assert.False(await session.ShownAsync("#bubble"));
    }

    [Fact]
    public async Task A_hover_panel_opens_at_once_over_its_trigger_stays_over_itself_and_closes_over_neither()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;

        await page.HoverAsync("#hover-trigger");
        var overTrigger = await session.ShownAsync("#panel");
        var locked = await page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overflow");
        var focus = await session.FocusAsync();
        await page.HoverAsync("#panel-link");
        var overPanel = await session.ShownAsync("#panel");
        await page.Mouse.MoveAsync(900, 600);

        Assert.True(overTrigger);
        Assert.Equal("visible", locked);
        Assert.Equal("BODY", focus);
        Assert.True(overPanel);
        Assert.False(await session.ShownAsync("#panel"));
    }

    [Fact]
    public async Task The_gap_between_a_hover_trigger_and_its_panel_belongs_to_neither()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;

        await page.HoverAsync("#hover-trigger");
        var box = await page.Locator("#hover-trigger").BoundingBoxAsync();
        await page.Mouse.MoveAsync(box!.X + 10, box.Y + box.Height + 5);

        // Flux's panel sat 10px under its trigger and closed on the first pixel of the gap.
        Assert.False(await session.ShownAsync("#panel"));
    }

    [Fact]
    public async Task A_press_on_a_hover_trigger_keeps_its_panel_and_Enter_opens_it_where_focus_alone_does_not()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#hover-trigger");
        var afterPress = await session.ShownAsync("#panel");
        await page.Mouse.MoveAsync(900, 600);
        await session.TabToAsync("#hover-trigger");
        var onFocus = await session.ShownAsync("#panel");
        await page.Keyboard.PressAsync("Enter");

        Assert.True(afterPress, "the trigger's own popovertarget toggled the hover-opened panel shut");
        Assert.False(onFocus);
        Assert.True(await session.ShownAsync("#panel"));
    }

    [Fact]
    public async Task A_hover_panel_with_a_condition_opens_only_while_its_root_matches_it()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;

        await page.HoverAsync("#rail-trigger");
        var expanded = await session.ShownAsync("#rail-menu");
        await page.Mouse.MoveAsync(900, 600);
        await page.CheckAsync("#collapsed");
        await page.HoverAsync("#rail-trigger");

        Assert.False(expanded);
        Assert.True(await session.ShownAsync("#rail-menu"));
    }

    [Fact]
    public async Task A_menu_the_pointer_opens_takes_no_focus_though_it_asks_for_it_and_one_Enter_opens_does()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;

        // The checkbox is pressed, so it has focus: what the reader was on before the pointer wandered.
        await page.CheckAsync("#collapsed");
        await page.HoverAsync("#rail-trigger");
        var shown = await session.ShownAsync("#rail-menu");
        var underThePointer = await session.FocusAsync();
        await page.Mouse.MoveAsync(900, 600);
        await page.FocusAsync("#rail-trigger");
        await page.Keyboard.PressAsync("Enter");

        Assert.True(shown);
        // Flux's rail menu, 2026-10-09: document.activeElement was the same element before and after.
        Assert.Equal("collapsed", underThePointer);
        Assert.Equal("rail-menu", await session.FocusAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.getElementById('rail-menu').hasAttribute('autofocus')"));
    }

    [Fact]
    public async Task The_pointer_leaving_a_menu_darkens_the_row_it_lit_and_not_the_row_that_has_focus()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;
        const string lit = "() => [...document.querySelectorAll('#menu > [data-active]')].map(r => r.id).join(',')";

        await page.HoverAsync("#row-c");
        await page.Mouse.MoveAsync(900, 600);
        var pointers = await page.EvaluateAsync<string>(lit);
        await page.HoverAsync("#row-c");
        // The keyboard moves on while the pointer rests: a render lights its row, and focus is on it.
        await page.EvaluateAsync("() => { document.getElementById('row-c').removeAttribute('data-active'); const d = document.getElementById('row-d'); d.setAttribute('data-active', ''); d.focus(); }");
        await page.Mouse.MoveAsync(900, 600);

        Assert.Equal(string.Empty, pointers);
        // Flux: the pointer leaving took nothing from the row the arrows were on.
        Assert.Equal("row-d", await page.EvaluateAsync<string>(lit));
    }

    [Fact]
    public async Task A_pointer_moving_about_inside_its_row_does_not_take_the_light_back_from_the_keyboard()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;
        var row = (await page.Locator("#row-d").BoundingBoxAsync())!;

        await page.Mouse.MoveAsync(row.X + 100, row.Y + (row.Height / 2));
        var entered = await page.EvaluateAsync<string>("() => document.querySelector('#menu > [data-active]').id");
        // The arrows go on to another row while the pointer rests where it is.
        await page.EvaluateAsync("() => { document.getElementById('row-d').removeAttribute('data-active'); document.getElementById('row-c').setAttribute('data-active', ''); }");
        await page.Mouse.MoveAsync(row.X + 8, row.Y + (row.Height / 2), new Microsoft.Playwright.MouseMoveOptions { Steps = 6 });

        Assert.Equal("row-d", entered);
        // Flux: 3 px along the same row, onto its icon, and the lit row was still the keyboard's.
        Assert.Equal("row-c", await page.EvaluateAsync<string>("() => document.querySelector('#menu > [data-active]').id"));
    }

    [Fact]
    public async Task Focus_follows_the_row_given_the_tab_stop_and_leaves_a_lit_row_that_says_it_is_the_pointers()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;
        await session.HooksLoadedAsync();
        const string settle = "() => new Promise(done => requestAnimationFrame(() => done()))";

        await page.ClickAsync("#picker-open");
        var opened = await session.FocusAsync();
        // What a render writes for a row the pointer lit: lit, and still no tab stop.
        await page.EvaluateAsync("() => document.getElementById('pick-a').setAttribute('data-active', '')");
        await page.EvaluateAsync(settle);
        var pointerLit = await session.FocusAsync();
        // And for the row the keyboard arrives on: the tab stop.
        await page.EvaluateAsync("() => document.getElementById('pick-a').setAttribute('tabindex', '0')");
        await page.EvaluateAsync(settle);
        var keyboards = await session.FocusAsync();
        // A menu that keeps no roving tab stop at all is followed by data-active alone, as it always was.
        await page.EvaluateAsync("() => document.getElementById('pick-c').setAttribute('data-active', '')");
        await page.EvaluateAsync(settle);

        Assert.Equal("picker", opened);
        Assert.Equal("picker", pointerLit);
        Assert.Equal("pick-a", keyboards);
        Assert.Equal("pick-c", await session.FocusAsync());
    }

    [Fact]
    public async Task The_row_under_the_pointer_is_the_only_lit_row_and_none_is_when_the_pointer_leaves_the_menu()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;
        const string lit = "() => [...document.querySelectorAll('#menu > [data-active], #menu > span > [data-active]')].map(r => r.id).join(',')";

        await page.HoverAsync("#row-c");
        var overThird = await page.EvaluateAsync<string>(lit);
        await page.HoverAsync("#row-disabled");
        var overDisabled = await page.EvaluateAsync<string>(lit);
        await page.Mouse.MoveAsync(900, 600);

        // #row-a was rendered lit — the keyboard cursor — and gave way to the pointer.
        Assert.Equal("row-c", overThird);
        Assert.Equal("row-c", overDisabled);
        Assert.Equal(string.Empty, await page.EvaluateAsync<string>(lit));
    }

    [Fact]
    public async Task The_diagonal_towards_an_open_submenu_does_not_touch_the_rows_it_crosses()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;
        var row = (await page.Locator("#row-sub").BoundingBoxAsync())!;
        var flyout = (await page.Locator("#flyout").BoundingBoxAsync())!;
        await page.EvaluateAsync("() => { window.__over = []; document.addEventListener('pointerover', e => { const r = e.target.closest('[role=menuitem]'); if (r) window.__over.push(r.id); }, true); }");

        // From the left end of the submenu's row to the bottom of its flyout: straight across the rows below.
        await page.Mouse.MoveAsync(row.X + 12, row.Y + row.Height / 2);
        for (var i = 1; i <= 12; i++)
        {
            await page.Mouse.MoveAsync(
                row.X + 12 + ((flyout.X + 10 - row.X - 12) * i / 12),
                row.Y + (row.Height / 2) + ((flyout.Y + flyout.Height - 8 - row.Y - (row.Height / 2)) * i / 12));
        }

        var crossed = await page.EvaluateAsync<string[]>("() => window.__over");
        Assert.DoesNotContain("row-c", crossed);
        Assert.DoesNotContain("row-d", crossed);
        Assert.Contains("fly-d", crossed);
        Assert.Equal(0, await page.Locator("#row-sub > span[data-rask-managed]").CountAsync());
    }

    [Fact]
    public async Task A_move_that_is_not_towards_the_submenu_reaches_the_next_row_at_once()
    {
        await using var session = await HookSession.OpenAsync<PointerHookPage>(playwright);
        var page = session.Page;
        var row = (await page.Locator("#row-sub").BoundingBoxAsync())!;

        await page.Mouse.MoveAsync(row.X + 12, row.Y + (row.Height / 2));
        await page.Mouse.MoveAsync(row.X + 14, row.Y + (row.Height / 2));
        await page.Mouse.MoveAsync(row.X + 12, row.Y + row.Height + 10);

        // Flux lit the next row 26 ms after the pointer left the submenu's, with no grace period.
        Assert.Equal("row-c", await page.EvaluateAsync<string>("() => document.querySelector('#menu [data-active]').id"));
    }

    private static async Task<float> CenterX(Microsoft.Playwright.IPage page, string selector)
    {
        var box = (await page.Locator(selector).BoundingBoxAsync())!;
        return box.X + (box.Width / 2);
    }

    private static async Task<float> CenterY(Microsoft.Playwright.IPage page, string selector)
    {
        var box = (await page.Locator(selector).BoundingBoxAsync())!;
        return box.Y + (box.Height / 2);
    }
}

/// <summary>The attributes of the pointer hooks, written out by hand.</summary>
public sealed partial class PointerHookPage : Component
{
    private const string Html = """
        <style>
          body { margin: 0; font: 14px/20px sans-serif; }
          [popover] { margin: 0; border: 1px solid #999; padding: 4px; }
          #menu, #flyout { position: fixed; display: block; width: 160px; padding: 0; }
          [role=menuitem] { display: block; width: 100%; height: 30px; box-sizing: border-box; border: 0; text-align: left; }
          [data-active] { background: #ddd; }
        </style>
        <span id="tip" data-rask-tooltip="bubble" style="position:fixed;left:20px;top:20px">
          <button id="tip-trigger" type="button" aria-expanded="false" aria-controls="bubble">?</button>
          <div id="bubble" popover="manual" role="tooltip" style="position:fixed;left:20px;top:60px">Help</div>
        </span>
        <div style="position:fixed;left:200px;top:20px;width:120px;height:40px;overflow:hidden">
          <span data-rask-tooltip="plain-bubble">
            <span id="plain-trigger" tabindex="0">hover me</span>
            <div id="plain-bubble" popover="manual" role="tooltip" style="position:fixed;left:200px;top:80px">Outside the card</div>
          </span>
        </div>
        <div id="hover" data-rask-hover="panel" style="position:fixed;left:20px;top:140px">
          <button id="hover-trigger" type="button" popovertarget="panel" style="height:30px">Preview</button>
          <div id="panel" popover style="position:fixed;left:20px;top:180px;width:200px;height:80px">
            <a id="panel-link" href="#">Open</a>
          </div>
        </div>
        <input id="collapsed" type="checkbox" style="position:fixed;left:400px;top:20px">
        <div id="rail" data-rask-hover="rail-menu" data-rask-hover-if="input:checked ~ *" style="position:fixed;left:400px;top:60px">
          <button id="rail-trigger" type="button" popovertarget="rail-menu">Rail item</button>
          <div id="rail-menu" popover role="menu" tabindex="-1" autofocus style="position:fixed;left:400px;top:100px">Menu</div>
        </div>
        <button id="picker-open" type="button" popovertarget="picker" aria-haspopup="true" style="position:fixed;left:600px;top:20px">Pick</button>
        <div id="picker" popover role="menu" tabindex="-1" autofocus style="position:fixed;left:600px;top:60px;width:160px">
          <button id="pick-a" type="button" role="menuitem" tabindex="-1">One</button>
          <button id="pick-b" type="button" role="menuitem" tabindex="-1">Two</button>
          <button id="pick-c" type="button" role="menuitem">Three</button>
        </div>
        <div id="menu" role="menu" tabindex="-1" data-rask-menu-pointer style="left:20px;top:320px">
          <button id="row-a" type="button" role="menuitem" data-active>New</button>
          <span>
            <button id="row-sub" type="button" role="menuitem" aria-haspopup="menu" data-rask-safe-area="flyout">Sort by</button>
            <div id="flyout" role="menu" style="left:180px;top:350px">
              <button id="fly-a" type="button" role="menuitem">Name</button>
              <button id="fly-b" type="button" role="menuitem">Date</button>
              <button id="fly-c" type="button" role="menuitem">Size</button>
              <button id="fly-d" type="button" role="menuitem">Kind</button>
            </div>
          </span>
          <button id="row-c" type="button" role="menuitem">Filter</button>
          <button id="row-d" type="button" role="menuitem"><span id="row-d-icon" style="display:inline-block;width:24px">x</span>Delete</button>
          <button id="row-disabled" type="button" role="menuitem" aria-disabled="true">Archive</button>
        </div>
        """;

    protected override Component? HeadAssets => Markup.Title["pointer hooks"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[Raw.Value(Html)];
}
