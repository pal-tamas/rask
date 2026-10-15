using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A colour picker kept in the browser (<c>data-rask-color</c>), in a real browser, on a page a session drives.
/// </summary>
/// <remarks>
///     The colours are Flux UI's, recorded on its live picker: the area steps by 2 and by 10 with Shift, the page
///     keys and Home / End go to its edges, a slider steps by 1 and by 10, a typed colour is taken on Enter or on
///     leaving the field, a swatch at a press. Its element announces `change` for each of those and for the end
///     of a drag, and nothing for opening, closing, a drag still held or a character typed — which is what the
///     page is told here (<c>#changes</c>).
/// </remarks>
public sealed class RuntimeHookColorTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Opener = "[data-ui-color-picker-swatch-button]";
    private const string Thumb = "[data-ui-color-picker-area-thumb]";
    private const string Hue = "[data-channel=hue] input[type=range]";
    private const string Trigger = "#color";
    private const string Inside = "[data-ui-color-picker-popover-input]";
    private const string Swatch = "[data-ui-color-picker-swatch]";

    [Fact]
    public async Task The_area_steps_by_two_and_by_ten_with_Shift_and_each_key_tells_the_page()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync(Opener);

        var seen = new List<string>();
        foreach (var key in new[] { "ArrowRight", "ArrowDown", "Shift+ArrowLeft", "Shift+ArrowUp" })
        {
            await page.Keyboard.PressAsync(key);
            seen.Add(await page.InputValueAsync(Inside));
        }

        await Expect(page.Locator("#seen")).ToHaveTextAsync("#d0ee70");
        Assert.Equal(["#b9da51", "#b5d54f", "#bad564", "#d0ee70"], seen);
        await Expect(page.Locator("#changes")).ToHaveTextAsync("4");
        Assert.Equal("#d0ee70", await page.InputValueAsync(Trigger));
    }

    [Fact]
    public async Task The_page_keys_and_Home_and_End_go_to_the_edges_and_black_keeps_its_hue()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync(Opener);

        var seen = new List<string>();
        foreach (var key in new[] { "PageUp", "End", "Home", "PageDown" })
        {
            await page.Keyboard.PressAsync(key);
            seen.Add(await page.InputValueAsync(Inside));
        }

        Assert.Equal(["#daff63", "#c2ff00", "#ffffff", "#000000"], seen);
        Assert.Equal("74", await page.InputValueAsync(Hue));
        Assert.Equal("Open color picker, Black", await page.GetAttributeAsync(Opener, "aria-label"));
        Assert.Equal("Black", await page.GetAttributeAsync(Swatch + "[aria-selected=true]", "aria-label"));
        Assert.Equal("0", await page.GetAttributeAsync(Thumb, "aria-valuenow"));
        await Expect(page.Locator("#seen")).ToHaveTextAsync("#000000");
    }

    [Fact]
    public async Task A_slider_steps_by_one_and_by_ten_with_Shift()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync(Opener);
        await page.FocusAsync(Hue);

        await page.Keyboard.PressAsync("ArrowRight");
        var one = await page.InputValueAsync(Hue);
        await page.Keyboard.PressAsync("Shift+ArrowLeft");

        Assert.Equal("75", one);
        Assert.Equal("65", await page.InputValueAsync(Hue));
        await Expect(page.Locator("#seen")).ToHaveTextAsync("#cfda55");
    }

    [Fact]
    public async Task A_drag_tells_the_page_nothing_while_it_is_held_and_once_when_it_lets_go()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync(Opener);
        var box = (await page.Locator("[data-ui-color-picker-area]").BoundingBoxAsync())!;

        await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 4));
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + (box.Width * 0.7f), box.Y + (box.Height / 4), new() { Steps = 6 });
        await page.WaitForTimeoutAsync(300);
        var held = (await page.TextContentAsync("#changes"), await page.InputValueAsync(Inside));
        await page.Mouse.UpAsync();

        Assert.Equal("0", held.Item1);
        Assert.NotEqual("#bada55", held.Item2);
        await Expect(page.Locator("#changes")).ToHaveTextAsync("1");
        await Expect(page.Locator("#seen")).ToHaveTextAsync(held.Item2);
    }

    [Fact]
    public async Task A_typed_colour_is_taken_on_Enter_or_on_leaving_and_text_that_is_no_colour_is_put_back()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.FillAsync(Trigger, string.Empty);
        await page.Locator(Trigger).PressSequentiallyAsync("#f00");
        await page.WaitForTimeoutAsync(300);
        var typing = await page.TextContentAsync("#changes");
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#seen")).ToHaveTextAsync("#ff0000");
        var left = await page.EvaluateAsync<bool>("s => document.activeElement !== document.querySelector(s)", Trigger);
        await page.FillAsync(Trigger, "hsl(120 50% 50%)");
        await page.Keyboard.PressAsync("Tab");
        await Expect(page.Locator("#seen")).ToHaveTextAsync("#40bf40");
        await page.FillAsync(Trigger, "zzz");
        await page.Keyboard.PressAsync("Enter");

        Assert.Equal("0", typing);
        Assert.True(left);
        await Expect(page.Locator(Trigger)).ToHaveValueAsync("#40bf40");
        await Expect(page.Locator("#changes")).ToHaveTextAsync("2");
    }

    [Fact]
    public async Task A_swatch_is_taken_at_a_press_announced_and_leaves_the_tab_stop_until_it_opens_again()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync(Opener);

        await page.ClickAsync(Swatch + "[aria-label=Teal]");
        var stop = await page.GetAttributeAsync(Swatch + "[tabindex='0']", "aria-label");
        await page.Keyboard.PressAsync("Escape");
        await page.ClickAsync(Opener);

        await Expect(page.Locator("#seen")).ToHaveTextAsync("#14b8a6");
        Assert.Equal("Red", stop);
        Assert.Equal("Teal selected", await page.TextContentAsync("[data-ui-color-picker-announce]"));
        Assert.Equal("Teal", await page.GetAttributeAsync(Swatch + "[tabindex='0']", "aria-label"));
        Assert.Equal("Open color picker, Teal", await page.GetAttributeAsync(Opener, "aria-label"));
    }

    [Fact]
    public async Task The_arrows_walk_the_swatches_eight_to_a_row_and_stop_at_the_edges_and_Enter_picks()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync(Opener);
        await page.FocusAsync(Swatch + "[aria-label=Red]");

        var walked = new List<string?>();
        foreach (var key in new[] { "ArrowDown", "ArrowRight", "End", "ArrowRight", "ArrowDown", "Home", "ArrowRight", "ArrowRight", "ArrowRight", "ArrowRight", "ArrowRight", "ArrowRight" })
        {
            await page.Keyboard.PressAsync(key);
            walked.Add(await page.EvaluateAsync<string?>("() => document.activeElement.getAttribute('aria-label')"));
        }

        await page.Keyboard.PressAsync("Enter");

        Assert.Equal(["Purple", "Pink", "White", "White", "White", "Red", "Orange", "Amber", "Yellow", "Green", "Teal", "Sky"], walked);
        await Expect(page.Locator("#seen")).ToHaveTextAsync("#0ea5e9");
        await Expect(page.Locator("#changes")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task Opening_and_closing_are_the_browsers_and_tell_the_page_nothing()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.FocusAsync(Trigger);
        await page.Keyboard.PressAsync("ArrowDown");
        await Expect(page.Locator(Trigger)).ToHaveAttributeAsync("aria-expanded", "true");
        var open = (await page.GetAttributeAsync(Opener, "aria-expanded"), await page.GetAttributeAsync("[data-ui-color-picker]", "data-open"));
        await page.Keyboard.PressAsync("Escape");
        await page.ClickAsync(Opener);
        await page.Mouse.ClickAsync(990, 690);
        await page.WaitForTimeoutAsync(300);

        Assert.Equal(("true", string.Empty), open);
        Assert.Equal("false", await page.GetAttributeAsync(Trigger, "aria-expanded"));
        Assert.Null(await page.GetAttributeAsync("[data-ui-color-picker]", "data-open"));
        Assert.False(await page.EvaluateAsync<bool>("() => document.querySelector('dialog').open"));
        Assert.Equal("0", await page.TextContentAsync("#changes"));
    }

    [Fact]
    public async Task On_the_sheet_a_change_waits_for_Select_and_Cancel_drops_it()
    {
        await using var session = await OpenAsync(new() { ViewportSize = new() { Width = 390, Height = 844 }, HasTouch = true });
        var page = session.Page;
        await page.ClickAsync(Opener);
        await page.FocusAsync(Thumb);

        await page.Keyboard.PressAsync("ArrowRight");
        await page.FocusAsync(Hue);
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await page.WaitForTimeoutAsync(300);
        var held = (await page.TextContentAsync("#changes"), await page.InputValueAsync(Trigger), await page.InputValueAsync(Inside));
        await page.ClickAsync("dialog [role=button]");
        await Expect(page.Locator("#seen")).ToHaveTextAsync(held.Item3);
        await page.ClickAsync(Opener);
        await page.FocusAsync(Thumb);
        await page.Keyboard.PressAsync("ArrowRight");
        await page.ClickAsync("dialog [data-ui-button]:not([data-ui-group-target])");
        await page.WaitForTimeoutAsync(300);

        Assert.Equal(("0", "#bada55"), (held.Item1, held.Item2));
        Assert.NotEqual("#bada55", held.Item3);
        Assert.Equal(held.Item3, await page.InputValueAsync(Trigger));
        Assert.Equal("1", await page.TextContentAsync("#changes"));
        Assert.Equal("10", await page.GetAttributeAsync("[data-channel=hue]", "data-rask-drag-inset"));
    }

    [Fact]
    public async Task On_the_sheet_a_swatch_is_taken_at_once_and_closes_it()
    {
        await using var session = await OpenAsync(new() { ViewportSize = new() { Width = 390, Height = 844 }, HasTouch = true });
        var page = session.Page;
        await page.ClickAsync(Opener);

        await page.ClickAsync(Swatch + "[aria-label=Yellow]");

        await Expect(page.Locator("#seen")).ToHaveTextAsync("#eab308");
        Assert.False(await page.EvaluateAsync<bool>("() => document.querySelector('dialog').open"));
    }

    [Fact]
    public async Task The_clear_button_empties_the_picker_and_goes_out_of_sight()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync("[aria-label='Clear color']");

        await Expect(page.Locator("#seen")).ToHaveTextAsync(string.Empty);
        await Expect(page.Locator("[aria-label='Clear color']")).ToHaveAttributeAsync("hidden", string.Empty);
        Assert.Equal(string.Empty, await page.InputValueAsync(Trigger));
        Assert.Equal("Open color picker", await page.GetAttributeAsync(Opener, "aria-label"));
    }

    private async Task<HookSession> OpenAsync(BrowserNewContextOptions? options = null)
    {
        var session = await HookSession.OpenAsync<ColorHookPage>(playwright, options);
        await session.HooksLoadedAsync();
        return session;
    }
}

/// <summary>A colour picker whose page hears every colour it takes, and counts them.</summary>
public sealed partial class ColorHookPage : Component
{
    private string _color = "#bada55";
    private int _changes;

    protected override Component? HeadAssets => [Markup.Title["colour hook"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Div.Style("padding:16px;max-width:320px")[
            Ui.ColorPicker.Value(_color).Id("color").Clearable().OnChange(c => (_color, _changes) = (c, _changes + 1)),
            Span.Id("seen")[_color],
            Span.Id("changes")[_changes.ToString(System.Globalization.CultureInfo.InvariantCulture)]
        ];
}
