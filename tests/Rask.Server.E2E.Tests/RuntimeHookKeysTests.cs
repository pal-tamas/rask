using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The hooks for a widget's own keys and its focus — the keys it keeps from the browser, a radio group one
///     arrow walks, the active option brought into view, focus that follows a rendered tab stop and a press
///     that leaves focus alone — in a real browser.
/// </summary>
/// <remarks>
///     Measured on Flux UI's live calendar, colour picker, pillbox, select and date picker pages on 2026-10-07.
///     No two of its widgets keep the same keys: the calendar grid keeps the arrows, Home, End and the paging
///     keys but not Space; a pillbox trigger keeps Enter, Space and the vertical arrows. Its date picker's
///     presets wrap at both ends and select as they go. Its calendar carries focus to the new day on an arrow
///     key and lets it fall to <c>&lt;body&gt;</c> on Home, End, PageUp and PageDown. A press on its colour
///     area leaves focus where it was. The test plays the page here: it moves the marks a render would move.
/// </remarks>
public sealed class RuntimeHookKeysTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string RecordKeys = "() => { window.keys = []; document.addEventListener('keydown', e => window.keys.push((e.key === ' ' ? 'Space' : e.key) + (e.defaultPrevented ? '!' : ''))); }";
    private const string Keys = "() => window.keys.join(' ')";

    [Fact]
    public async Task A_widget_keeps_the_keys_it_lists_and_the_page_still_hears_them()
    {
        await using var session = await HookSession.OpenAsync<KeysHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync(RecordKeys);

        await page.FocusAsync("#grid");
        foreach (var key in new[] { "ArrowDown", "ArrowLeft", "Home", "End", "PageDown", "PageUp" })
        {
            await page.Keyboard.PressAsync(key);
        }

        var kept = await page.EvaluateAsync<double>("() => scrollY");
        await page.Keyboard.PressAsync("Space");

        Assert.Equal(0, kept);
        Assert.Equal("ArrowDown! ArrowLeft! Home! End! PageDown! PageUp! Space", await page.EvaluateAsync<string>(Keys));
        // Space is not on the grid's list and scrolls the page, as it does under Flux's calendar.
        await page.WaitForFunctionAsync("() => scrollY > 0", null, new() { Timeout = 5_000 });
    }

    [Fact]
    public async Task A_key_typed_into_a_text_field_inside_the_widget_or_held_with_Control_is_left_alone()
    {
        await using var session = await HookSession.OpenAsync<KeysHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync(RecordKeys);

        await page.FocusAsync("#grid-text");
        await page.Keyboard.PressAsync("Home");
        await page.FocusAsync("#grid");
        await page.Keyboard.PressAsync("Control+Home");
        await page.FocusAsync("#plain");
        await page.Keyboard.PressAsync("End");

        Assert.Equal("Home Control Home End", await page.EvaluateAsync<string>(Keys));
    }

    [Fact]
    public async Task A_trigger_lists_its_own_keys_and_the_listbox_button_of_round_one_is_one_such_list()
    {
        await using var session = await HookSession.OpenAsync<KeysHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync(RecordKeys);

        await page.FocusAsync("#trigger");
        foreach (var key in new[] { "Enter", "Space", "ArrowDown", "ArrowUp", "Home" })
        {
            await page.Keyboard.PressAsync(key);
        }

        await page.FocusAsync("#select");
        foreach (var key in new[] { "Enter", "ArrowDown", "End" })
        {
            await page.Keyboard.PressAsync(key);
        }

        Assert.Equal("Enter! Space! ArrowDown! ArrowUp! Home Enter! ArrowDown! End", await page.EvaluateAsync<string>(Keys));
    }

    [Fact]
    public async Task An_arrow_in_a_roving_radio_group_focuses_and_presses_the_next_radio_and_wraps_at_both_ends()
    {
        await using var session = await HookSession.OpenAsync<KeysHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync("() => { window.pressed = []; document.getElementById('presets').addEventListener('click', e => window.pressed.push(e.target.id)); }");

        await page.FocusAsync("#r1");
        await page.Keyboard.PressAsync("ArrowDown");
        var afterDown = await session.FocusAsync();
        var stops = await page.EvaluateAsync<string>("() => [...document.querySelectorAll('#presets [role=radio]')].map(r => r.tabIndex).join(' ')");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowRight");
        var wrappedForward = await session.FocusAsync();
        await page.Keyboard.PressAsync("ArrowUp");

        Assert.Equal("r2", afterDown);
        Assert.Equal("-1 0 -1", stops);
        Assert.Equal("r1", wrappedForward);
        Assert.Equal("r3", await session.FocusAsync());
        Assert.Equal("r2 r3 r1 r3", await page.EvaluateAsync<string>("() => window.pressed.join(' ')"));
        Assert.Equal(0, await page.EvaluateAsync<double>("() => scrollY"));
    }

    [Fact]
    public async Task The_active_option_is_brought_into_view_inside_its_list_by_the_least_movement_and_the_page_stays()
    {
        await using var session = await HookSession.OpenAsync<KeysHookPage>(playwright);
        var page = session.Page;
        const string activate = "id => new Promise(done => { const l = document.getElementById('list'); l.setAttribute('aria-activedescendant', id); requestAnimationFrame(() => done(l.scrollTop)); })";

        var below = await page.EvaluateAsync<double>(activate, "o10");
        var inView = await page.EvaluateAsync<double>(activate, "o9");
        var above = await page.EvaluateAsync<double>(activate, "o2");

        // Rows of 30 px in a list 100 px tall: row 10 ends at 330, so the list shows 230–330.
        Assert.Equal(230, below);
        Assert.Equal(230, inView);
        Assert.Equal(60, above);
        Assert.Equal(0, await page.EvaluateAsync<double>("() => scrollY"));
    }

    [Fact]
    public async Task Focus_on_the_tab_stop_follows_it_when_a_render_moves_the_mark_or_replaces_the_element()
    {
        await using var session = await HookSession.OpenAsync<KeysHookPage>(playwright);
        var page = session.Page;
        const string settle = "() => new Promise(done => requestAnimationFrame(() => done(document.activeElement.id || document.activeElement.tagName)))";

        await page.FocusAsync("#d1");
        await page.EvaluateAsync("() => { d1.removeAttribute('data-rask-focus-target'); d2.setAttribute('data-rask-focus-target', ''); }");
        var moved = await page.EvaluateAsync<string>(settle);
        await page.EvaluateAsync("() => { days.innerHTML = '<button id=n1>1</button><button id=n2 data-rask-focus-target>2</button>'; }");
        var replaced = await page.EvaluateAsync<string>(settle);

        Assert.Equal("d2", moved);
        Assert.Equal("n2", replaced);
    }

    [Fact]
    public async Task Focus_elsewhere_is_never_taken_and_a_render_that_leaves_no_tab_stop_lets_it_fall_to_the_body()
    {
        await using var session = await HookSession.OpenAsync<KeysHookPage>(playwright);
        var page = session.Page;
        const string settle = "() => new Promise(done => requestAnimationFrame(() => done(document.activeElement.id || document.activeElement.tagName)))";

        await page.FocusAsync("#prev");
        await page.EvaluateAsync("() => { d1.removeAttribute('data-rask-focus-target'); d2.setAttribute('data-rask-focus-target', ''); }");
        var onAnotherControl = await page.EvaluateAsync<string>(settle);
        await page.FocusAsync("#d2");
        await page.EvaluateAsync("() => { days.innerHTML = '<button id=n1>1</button>'; }");
        var dropped = await page.EvaluateAsync<string>(settle);
        await page.EvaluateAsync("() => { document.getElementById('n1').setAttribute('data-rask-focus-target', ''); }");

        Assert.Equal("prev", onAnotherControl);
        Assert.Equal("BODY", dropped);
        Assert.Equal("BODY", await page.EvaluateAsync<string>(settle));
    }

    [Fact]
    public async Task A_tab_stop_replaced_with_no_successor_does_not_hand_focus_to_another_containers_tab_stop()
    {
        await using var session = await HookSession.OpenAsync<KeysHookPage>(playwright);
        var page = session.Page;

        await page.FocusAsync("#d1");
        await page.EvaluateAsync("() => { days.innerHTML = '<button id=n1 tabindex=0>1</button><button id=n2>2</button>'; }");
        var paged = await page.EvaluateAsync<string>("() => new Promise(done => setTimeout(() => done(document.activeElement.id || document.activeElement.localName), 50))");

        // Flux's calendar on a paging key, on a page that shows a second calendar: the focus falls to the page.
        Assert.Equal("body", paged);
    }

    [Fact]
    public async Task A_press_on_an_element_that_keeps_focus_leaves_focus_where_it_was()
    {
        await using var session = await HookSession.OpenAsync<KeysHookPage>(playwright);
        var page = session.Page;

        await page.FocusAsync("#prev");
        await page.ClickAsync("#area");
        var afterArea = await session.FocusAsync();
        await page.ClickAsync("#plain");

        Assert.Equal("prev", afterArea);
        Assert.Equal("plain", await session.FocusAsync());
    }
}

/// <summary>Widgets written out by hand, on a page tall enough to scroll.</summary>
public sealed partial class KeysHookPage : Component
{
    private static readonly string Options = string.Concat(Enumerable.Range(0, 20).Select(i => $"<div id=\"o{i}\" role=\"option\" style=\"height:30px\">{i}</div>"));

    private static readonly string Html = $$"""
        <div id="grid" role="grid" tabindex="0" data-rask-contain-keys="Arrows Home End PageUp PageDown">
            <input id="grid-text" aria-label="month">
        </div>
        <div id="trigger" role="combobox" tabindex="0" aria-haspopup="listbox" aria-expanded="false"
             data-rask-contain-keys="Enter Space ArrowUp ArrowDown">Choose</div>
        <button id="select" type="button" role="combobox" aria-haspopup="listbox" aria-expanded="false" data-rask-listbox-button>Choose</button>
        <div id="plain" tabindex="0">plain</div>
        <div id="presets" role="radiogroup" data-rask-roving>
            <button id="r1" type="button" role="radio" aria-checked="true" tabindex="0">Today</button>
            <button id="r2" type="button" role="radio" aria-checked="false" tabindex="-1">Yesterday</button>
            <button id="r3" type="button" role="radio" aria-checked="false" tabindex="-1">This week</button>
        </div>
        <div id="list" role="listbox" tabindex="0" aria-activedescendant="o0" style="height:100px;overflow-y:auto">{{Options}}</div>
        <div id="cal" data-rask-focus-follows>
            <button id="prev" type="button">Previous month</button>
            <div id="days"><button id="d1" tabindex="0" data-rask-focus-target>1</button><button id="d2" tabindex="-1">2</button></div>
        </div>
        <div id="cal2" data-rask-focus-follows><button id="e1" tabindex="0" data-rask-focus-target>1</button></div>
        <div id="area" data-rask-press-keeps-focus tabindex="-1" style="width:100px;height:100px;background:#ccc"></div>
        <div style="height:3000px"></div>
        """;

    protected override Component? HeadAssets => Markup.Title["key hooks"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[Raw.Value(Html)];
}
