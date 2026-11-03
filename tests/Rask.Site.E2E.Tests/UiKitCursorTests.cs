using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The hand over what can be pressed, on the running showcase — an owner-approved departure from Flux
///     (2026-11-02): every pressable kit part shows <c>cursor: pointer</c>, and the arrow while it cannot be pressed.
/// </summary>
/// <remarks>
///     A cursor is read where the pointer actually lands: the DEEPEST element at the centre of the part
///     (<c>elementFromPoint</c>), which is what the browser draws the cursor for. A part whose own cursor is right
///     but whose label, icon or day number says <c>cursor-default</c> shows the arrow under the pointer — the
///     unit test of the sheet rule cannot see that, nor whether a utility on the part outranks it.
/// </remarks>
public sealed class UiKitCursorTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    // Runs in the page, on one element: brought on screen, and the cursor of whatever is under its centre — or
    // `outside` when that is not the element or inside it, which would make the reading about something else.
    private const string CursorAtCentre =
        "el => { el.scrollIntoView({ block: 'center', inline: 'center' }); const r = el.getBoundingClientRect(); "
        + "const hit = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2); "
        + "return hit && (hit === el || el.contains(hit)) ? getComputedStyle(hit).cursor : 'outside ' + (hit?.outerHTML.slice(0, 120) ?? 'nothing'); }";

    // Runs in the page, on one element: its own computed cursor, for a part the pointer cannot land on (it takes no
    // pointer events while disabled, so the hit is whatever is under it).
    private const string OwnCursor = "el => getComputedStyle(el).cursor";

    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Theory]
    [InlineData("button", "Button", "[data-testid='ui-button-variants'] [data-ui-button]")]
    [InlineData("button", "Button", "[data-testid='ui-button-icons'] [data-ui-button]")]
    [InlineData("badge", "Badge", "[data-testid='ui-badge-close'] [data-ui-badge-close]")]
    [InlineData("dropdown", "Dropdown", "[data-testid='ui-dropdown-intro'] [data-ui-button]")]
    [InlineData("tabs", "Tabs", "[data-testid='ui-tabs'] [role=tab]")]
    [InlineData("tabs", "Tabs", "[data-testid='ui-tabs-segmented'] [role=tab]")]
    [InlineData("tabs", "Tabs", "[data-testid='ui-tabs-pills'] [role=tab]")]
    [InlineData("select", "Select", "#ui-select-listbox")]
    [InlineData("select", "Select", "#ui-select-native")]
    [InlineData("pagination", "Pagination", "[data-testid='ui-pagination-size'] select")]
    [InlineData("pagination", "Pagination", "[data-testid='ui-pagination'] [data-ui-pagination] button >> visible=true")]
    [InlineData("checkbox", "Checkbox", "[data-testid='ui-checkbox'] [data-ui-checkbox]")]
    [InlineData("checkbox", "Checkbox", "[data-testid='ui-checkbox'] [data-ui-label]")]
    [InlineData("checkbox", "Checkbox", "[data-testid='ui-checkbox-cards'] [data-ui-checkbox-cards]")]
    [InlineData("checkbox", "Checkbox", "[data-testid='ui-checkbox-buttons'] [data-ui-checkbox-buttons]")]
    [InlineData("radio", "Radio", "[data-testid='ui-radio'] [data-ui-radio]")]
    [InlineData("radio", "Radio", "[data-testid='ui-radio-segmented'] [data-ui-radio-segmented]")]
    [InlineData("radio", "Radio", "[data-testid='ui-radio-cards'] [data-ui-radio-cards]")]
    [InlineData("switch", "Switch", "[data-testid='ui-switch'] [data-ui-switch]")]
    [InlineData("toggle", "Toggle", "[data-testid='ui-toggle-basic'] [data-ui-toggle]")]
    [InlineData("calendar", "Calendar", "[data-testid='ui-calendar'] [role=gridcell] button:not([disabled])")]
    [InlineData("calendar", "Calendar", "[data-testid='ui-calendar'] :is([data-rask-calendar-part=previous], [data-rask-calendar-part=next])")]
    [InlineData("accordion", "Accordion", "[data-testid='ui-accordion-basic'] summary")]
    [InlineData("navlist", "Navlist", "[data-testid='ui-navlist-collapsible'] summary")]
    [InlineData("kanban", "Kanban", "[data-testid='ui-kanban-buttons'] button[data-ui-kanban-card]")]
    [InlineData("carousel", "Carousel", "[data-testid='ui-carousel-basic'] button:not([disabled])")]
    public Task A_pressable_kit_part_shows_the_hand_where_the_pointer_lands(string slug, string heading, string selector) => RunAsync(async () =>
    {
        await OpenUiPageAsync(slug, heading);
        var parts = Page.Locator(selector);
        await Expect(parts.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var cursors = await CursorsAsync(parts);

        Assert.All(cursors, c => Assert.True(c.Cursor == "pointer", $"{slug}: {c.Part} shows `{c.Cursor}` where the pointer lands."));
    });

    [Fact]
    public Task An_open_menu_shows_the_hand_over_every_row_it_can_press() => RunAsync(async () =>
    {
        await OpenUiPageAsync("dropdown", "Dropdown");
        var scope = Page.Locator("[data-testid='ui-dropdown-checkbox']");
        await scope.Locator("[data-ui-button]").First.ClickAsync();
        var rows = scope.Locator("[role^=menuitem]:not([aria-disabled=true]):not([data-disabled])");
        await Expect(rows.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var cursors = await CursorsAsync(rows);

        Assert.All(cursors, c => Assert.True(c.Cursor == "pointer", $"menu row {c.Part} shows `{c.Cursor}`."));
    });

    [Fact]
    public Task An_open_listbox_shows_the_hand_over_its_options() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        await Page.Locator("#ui-select-listbox").ClickAsync();
        var options = Page.Locator("[data-testid='ui-listbox'] [role=option]:not([aria-disabled=true])");
        await Expect(options.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var cursors = await CursorsAsync(options);

        Assert.All(cursors, c => Assert.True(c.Cursor == "pointer", $"option {c.Part} shows `{c.Cursor}`."));
    });

    [Fact]
    public Task An_open_date_picker_shows_the_hand_over_its_days_and_steps() => RunAsync(async () =>
    {
        await OpenUiPageAsync("date-picker", "Date picker");
        var scope = Page.Locator("[data-testid='ui-date-picker']");
        await scope.Locator("button").First.ClickAsync();
        var days = scope.Locator("[role=gridcell] button:not([disabled])");
        await Expect(days.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var cursors = await CursorsAsync(days.Locator("visible=true"));

        Assert.All(cursors, c => Assert.True(c.Cursor == "pointer", $"day {c.Part} shows `{c.Cursor}`."));
    });

    [Fact]
    public Task What_cannot_be_pressed_now_shows_the_arrow() => RunAsync(async () =>
    {
        await OpenUiPageAsync("button", "Button");
        var button = Page.Locator("[data-testid='ui-button-variants'] [data-ui-button]").First;
        await Expect(button).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // Each state on the same button in turn: the attribute, then the arrow, then the hand back once it is gone.
        var states = new List<(string State, string Cursor)>();
        foreach (var (name, value) in new[] { ("disabled", ""), ("aria-disabled", "true"), ("data-disabled", ""), ("data-loading", ""), ("aria-busy", "true") })
        {
            await button.EvaluateAsync("(el, [n, v]) => el.setAttribute(n, v)", new[] { name, value });
            states.Add((name, await button.EvaluateAsync<string>(OwnCursor)));
            await button.EvaluateAsync("(el, n) => el.removeAttribute(n)", name);
        }

        await button.EvaluateAsync("el => el.parentElement.setAttribute('inert', '')");
        states.Add(("inside inert", await button.EvaluateAsync<string>(OwnCursor)));
        await button.EvaluateAsync("el => el.parentElement.removeAttribute('inert')");
        var after = await button.EvaluateAsync<string>(OwnCursor);

        Assert.All(states, s => Assert.True(s.Cursor == "default", $"a button {s.State} shows `{s.Cursor}`."));
        Assert.Equal("pointer", after);
    });

    [Fact]
    public Task A_disabled_kit_control_shows_the_arrow_where_the_pointer_lands() => RunAsync(async () =>
    {
        await OpenUiPageAsync("checkbox", "Checkbox");
        var disabled = Page.Locator("[data-testid='ui-checkbox-disabled']");
        await Expect(disabled).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        var off = disabled.Locator("[data-ui-checkbox]:has(input:disabled), [data-ui-field]:has(input:disabled) > [data-ui-label]");

        var cursors = await CursorsAsync(off);

        Assert.NotEmpty(cursors);
        Assert.All(cursors, c => Assert.True(c.Cursor is "default" or "auto", $"disabled {c.Part} shows `{c.Cursor}`."));
    });

    [Fact]
    public Task An_apps_own_button_gets_the_hand_and_a_plain_clickable_div_and_a_text_field_do_not() => RunAsync(async () =>
    {
        await OpenUiPageAsync("button", "Button");

        // The application's own markup, in a page whose sheet carries the kit: no kit class, no handler-driven cursor.
        await Page.Locator("main").EvaluateAsync(
            "main => main.insertAdjacentHTML('afterbegin', `<div id=own style='padding:8px'>"
            + "<button id=own-button>Own</button> <div id=own-div onclick='void 0' style='display:inline-block;padding:8px'>Div</div> "
            + "<label id=own-label><input type=checkbox> Own box</label> <label id=own-text-label>Name <input id=own-text></label> "
            + "<button id=own-utility class=cursor-text>Utility</button></div>`)");

        var button = await CursorAsync(Page.Locator("#own-button"));
        var div = await CursorAsync(Page.Locator("#own-div"));
        var label = await CursorAsync(Page.Locator("#own-label"));
        var text = await CursorAsync(Page.Locator("#own-text"));
        var utility = await CursorAsync(Page.Locator("#own-utility"));

        Assert.Equal("pointer", button);
        Assert.Equal("auto", div);
        Assert.Equal("pointer", label);
        Assert.Equal("text", text);
        Assert.Equal("text", utility);
    });

    [Fact]
    public Task The_collapsed_sidebar_rail_keeps_its_resize_cursor() => RunAsync(async () =>
    {
        await OpenUiPageAsync("sidebar", "Sidebar");

        var rails = await Page.EvaluateAsync<int>("() => [...document.styleSheets].flatMap(s => { try { return [...s.cssRules]; } catch { return []; } })"
            + ".map(r => r.cssText).join('').split('cursor: e-resize').length - 1");

        // The rail's own `sidebar-rail:cursor-e-resize` is a utility, above the sheet's rule: still in the sheet.
        Assert.True(rails > 0, "No `cursor: e-resize` rule reached the page for the collapsed sidebar's rail.");
    });

    private async Task<string> CursorAsync(ILocator element)
    {
        await Expect(element).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        return await element.EvaluateAsync<string>(CursorAtCentre);
    }

    private static async Task<List<(string Part, string Cursor)>> CursorsAsync(ILocator parts)
    {
        var read = new List<(string, string)>();
        var count = await parts.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var part = parts.Nth(i);
            if (!await part.IsVisibleAsync())
            {
                continue;
            }

            var label = await part.EvaluateAsync<string>("el => el.outerHTML.slice(0, 100)");
            read.Add((label, await part.EvaluateAsync<string>(CursorAtCentre)));
        }

        Assert.NotEmpty(read);
        return read;
    }
}
