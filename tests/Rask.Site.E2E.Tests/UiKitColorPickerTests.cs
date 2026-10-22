using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     Flux's color picker, in a browser: what only one can show — the dialog opening by command and closing by
///     itself, where it hangs, focus, the pointer dragging the area and a slider, the sheet a phone gets, and
///     the clipboard.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitColorPickerTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    // Each example's result, by the name the page gives it.
    private ILocator Demo(string name) => Page.Locator($"[data-testid='ui-color-picker-{name}']");

    private ILocator State => Page.Locator("[data-testid='ui-color-picker-state']");

    [Fact]
    public Task The_swatch_button_opens_a_modal_popover_under_the_trigger_and_focuses_the_area() => RunAsync(async () =>
    {
        await OpenAsync();
        var intro = Page.Locator("[data-testid='ui-color-picker']");
        var opener = intro.Locator("[data-ui-color-picker-swatch-button]");
        var dialog = intro.Locator("dialog");

        await opener.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(opener).ToHaveAttributeAsync("aria-expanded", "true");
        var trigger = await intro.Locator("[data-ui-color-picker-trigger]").BoundingBoxAsync();
        var popover = await dialog.BoundingBoxAsync();

        // Flux's gap is five pixels; the popover lines up with the trigger's start and is 260px wide.
        Assert.Equal(trigger!.X, popover!.X, 1);
        Assert.Equal(trigger.Y + trigger.Height + 5, popover.Y, 1);
        Assert.Equal(260, popover.Width, 1);
        Assert.True(await dialog.EvaluateAsync<bool>("d => d.matches(':modal')"));
        await Expect(intro.Locator("[data-ui-color-picker-area-thumb]")).ToBeFocusedAsync();
    });

    [Fact]
    public Task Escape_and_a_click_outside_close_the_popover_and_focus_returns_to_the_button() => RunAsync(async () =>
    {
        await OpenAsync();
        var intro = Page.Locator("[data-testid='ui-color-picker']");
        var opener = intro.Locator("[data-ui-color-picker-swatch-button]");
        var dialog = intro.Locator("dialog");

        await opener.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(opener).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(opener).ToBeFocusedAsync();
        await opener.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await Page.Mouse.ClickAsync(5, 300);

        await Expect(dialog).ToBeHiddenAsync();
        await Expect(opener).ToHaveAttributeAsync("aria-expanded", "false");
    });

    [Fact]
    public Task The_area_keys_change_the_colour_live_and_the_page_hears_it() => RunAsync(async () =>
    {
        await OpenAsync();
        var intro = Page.Locator("[data-testid='ui-color-picker']");

        await intro.Locator("[data-ui-color-picker-swatch-button]").ClickAsync();
        await Expect(intro.Locator("[data-ui-color-picker-area-thumb]")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowRight");

        // 61% saturation to 63%: #bada55 to #b9da51, as on Flux.
        await Expect(intro.Locator("[data-ui-color-picker-input]")).ToHaveValueAsync("#b9da51");
        await Expect(intro.Locator("[data-ui-color-picker-popover-input]")).ToHaveValueAsync("#b9da51");
        await Expect(State).ToContainTextAsync("Theme: #b9da51.");
    });

    [Fact]
    public Task A_typed_colour_is_normalised_on_Enter_and_the_input_is_left() => RunAsync(async () =>
    {
        await OpenAsync();
        var input = Page.Locator("[data-testid='ui-color-picker']").Locator("[data-ui-color-picker-input]");

        await input.FillAsync("rebeccapurple");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(input).ToHaveValueAsync("#663399");
        await Expect(State).ToContainTextAsync("Theme: #663399.");
        await Expect(input).Not.ToBeFocusedAsync();
    });

    [Fact]
    public Task A_wide_gamut_colour_becomes_what_fluxs_picker_makes_of_it() => RunAsync(async () =>
    {
        await OpenAsync();
        var input = Page.Locator("[data-testid='ui-color-picker']").Locator("[data-ui-color-picker-input]");

        await input.FillAsync("oklch(0.7 0.1 200)");
        await Page.Keyboard.PressAsync("Enter");

        // Lightness, chroma and hue read as red, green and blue: Flux's own answer, kept.
        await Expect(input).ToHaveValueAsync("#0100c8");
        await Expect(State).ToContainTextAsync("Theme: #0100c8.");
    });

    [Fact]
    public Task Text_that_is_no_colour_is_put_back_when_the_input_is_left() => RunAsync(async () =>
    {
        await OpenAsync();
        var input = Page.Locator("[data-testid='ui-color-picker']").Locator("[data-ui-color-picker-input]");

        await input.FillAsync("not a colour");
        await Page.Keyboard.PressAsync("Tab");

        await Expect(input).ToHaveValueAsync("#bada55");
        await Expect(State).ToContainTextAsync("Theme: #bada55.");
    });

    [Fact]
    public Task ArrowDown_in_the_input_opens_the_popover_and_Escape_returns_to_the_input() => RunAsync(async () =>
    {
        await OpenAsync();
        var intro = Page.Locator("[data-testid='ui-color-picker']");
        var input = intro.Locator("[data-ui-color-picker-input]");

        await input.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(intro.Locator("dialog")).ToBeVisibleAsync();
        await Expect(input).ToHaveAttributeAsync("aria-expanded", "true");
        await Page.Keyboard.PressAsync("Escape");

        await Expect(intro.Locator("dialog")).ToBeHiddenAsync();
        await Expect(input).ToBeFocusedAsync();
        await Expect(input).ToHaveAttributeAsync("aria-expanded", "false");
    });

    [Fact]
    public Task Dragging_the_area_follows_the_pointer_and_stops_at_its_edge() => RunAsync(async () =>
    {
        await OpenAsync();
        var intro = Page.Locator("[data-testid='ui-color-picker']");
        var thumb = intro.Locator("[data-ui-color-picker-area-thumb]");

        await intro.Locator("[data-ui-color-picker-swatch-button]").ClickAsync();
        var area = await intro.Locator("[data-ui-color-picker-area]").BoundingBoxAsync();
        await Page.Mouse.MoveAsync(area!.X + (area.Width / 2), area.Y + (area.Height / 2));
        await Page.Mouse.DownAsync();
        await Expect(thumb).ToHaveAttributeAsync("aria-valuetext", "Saturation 50%, brightness 50%");
        // Out past the top right corner: the area keeps the pointer, and the thumb stops in the corner.
        await Page.Mouse.MoveAsync(area.X + area.Width + 80, area.Y - 60, new MouseMoveOptions { Steps = 6 });
        await Expect(thumb).ToHaveAttributeAsync("aria-valuetext", "Saturation 100%, brightness 100%");
        await Page.Mouse.UpAsync();

        // Hue 74 at full saturation and brightness.
        await Expect(intro.Locator("[data-ui-color-picker-input]")).ToHaveValueAsync("#c2ff00");
    });

    [Fact]
    public Task A_press_on_the_hue_track_moves_the_hue_and_the_arrows_step_it() => RunAsync(async () =>
    {
        await OpenAsync();
        var intro = Page.Locator("[data-testid='ui-color-picker']");
        var hue = intro.Locator("[data-channel='hue'] input[type='range']");

        await intro.Locator("[data-ui-color-picker-swatch-button]").ClickAsync();
        var track = await intro.Locator("[data-channel='hue']").BoundingBoxAsync();
        await Page.Mouse.ClickAsync(track!.X + (track.Width / 2), track.Y + (track.Height / 2));
        await Expect(hue).ToHaveValueAsync("180");
        await Expect(hue).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(hue).ToHaveValueAsync("181");
        await Page.Keyboard.PressAsync("Shift+ArrowRight");

        await Expect(hue).ToHaveValueAsync("191");
        await Expect(intro.Locator("[data-ui-color-picker-input]")).ToHaveValueAsync("#55c2da");
    });

    [Fact]
    public Task A_swatch_sets_the_colour_and_the_arrows_walk_the_palette() => RunAsync(async () =>
    {
        await OpenAsync();
        var swatches = Demo("swatches").Locator("[data-ui-color-picker]").Last;
        var palette = swatches.Locator("[data-ui-color-picker-swatch]");
        var input = swatches.Locator("[data-ui-color-picker-input]");

        // The second of Flux's two palettes: three colours, each with a label.
        await swatches.Locator("[data-ui-color-picker-swatch-button]").ClickAsync();
        await Expect(palette).ToHaveCountAsync(3);
        await palette.Nth(2).ClickAsync();
        await Expect(input).ToHaveValueAsync("#3b82f6");
        await Expect(palette.Nth(2)).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(swatches.Locator("[data-ui-color-picker-announce]")).ToHaveTextAsync("Blue selected");
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(palette.Nth(1)).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Enter");

        await Expect(input).ToHaveValueAsync("#22c55e");
        await Expect(swatches.Locator("dialog")).ToBeVisibleAsync();
    });

    [Fact]
    public Task The_alpha_slider_writes_the_opacity_into_the_value() => RunAsync(async () =>
    {
        await OpenAsync();
        // The fourth of the six formats: rgba, which has an alpha slider.
        var format = Demo("format").Locator("[data-ui-color-picker]").Nth(3);

        await format.Locator("[data-ui-color-picker-swatch-button]").ClickAsync();
        await format.Locator("[data-channel='alpha'] input[type='range']").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");

        await Expect(format.Locator("[data-ui-color-picker-input]")).ToHaveValueAsync("rgb(99 102 241 / 0.51)");
    });

    [Fact]
    public Task The_button_trigger_opens_on_a_click_and_shows_the_picked_colour() => RunAsync(async () =>
    {
        await OpenAsync();
        var button = Demo("button");

        await button.Locator("[data-ui-color-picker-button]").ClickAsync();
        await Expect(button.Locator("dialog")).ToBeVisibleAsync();
        await button.Locator("[data-ui-color-picker-swatch]").First.ClickAsync();
        await Page.Keyboard.PressAsync("Escape");

        await Expect(button.Locator("[data-ui-color-picker-value]")).ToHaveTextAsync("#ef4444");
        await Expect(button.Locator("[data-ui-color-picker-button]")).ToBeFocusedAsync();
    });

    [Fact]
    public Task The_composed_popover_draws_only_the_parts_it_was_given() => RunAsync(async () =>
    {
        await OpenAsync();
        var layout = Demo("layout");

        await layout.Locator("[data-ui-color-picker-swatch-button]").ClickAsync();
        await Expect(layout.Locator("dialog")).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("ArrowLeft");

        await Expect(layout.Locator("[data-ui-color-picker-popover-input]")).ToBeVisibleAsync();
        await Expect(layout.Locator("[data-ui-color-picker-swatches]")).ToHaveCountAsync(0);
        await Expect(layout.Locator("[data-ui-color-picker-input]")).Not.ToHaveValueAsync("#facade");
    });

    [Fact]
    public Task Clear_empties_the_colour_and_a_disabled_picker_does_not_open() => RunAsync(async () =>
    {
        await OpenAsync();
        var clearable = Demo("clearable");
        var disabled = Demo("disabled");

        await clearable.Locator("[aria-label='Clear color']").ClickAsync();
        await Expect(Page.Locator("[data-testid='ui-color-picker-cleared']")).ToHaveTextAsync("Color: none.");
        await Expect(clearable.Locator("[data-ui-color-picker-input]")).ToHaveValueAsync("");
        await disabled.Locator("[data-ui-color-picker-swatch-button]").ClickAsync(new LocatorClickOptions { Force = true });

        await Expect(disabled.Locator("dialog")).ToBeHiddenAsync();
        await Expect(disabled.Locator("[data-ui-color-picker-input]")).ToBeDisabledAsync();
    });

    [Fact]
    public Task The_page_keys_on_the_area_and_the_arrows_on_a_swatch_do_not_scroll_the_page() => RunAsync(async () =>
    {
        await OpenAsync();
        var intro = Page.Locator("[data-testid='ui-color-picker']");

        await intro.Locator("[data-ui-color-picker-swatch-button]").ClickAsync();
        await Expect(intro.Locator("[data-ui-color-picker-area-thumb]")).ToBeFocusedAsync();
        var before = await Page.EvaluateAsync<double>("scrollY");
        await Page.Keyboard.PressAsync("PageDown");
        await Page.Keyboard.PressAsync("ArrowDown");
        await intro.Locator("[data-ui-color-picker-swatch]").First.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowDown");

        await Expect(intro.Locator("[data-ui-color-picker-swatch]").Nth(8)).ToBeFocusedAsync();
        Assert.Equal(before, await Page.EvaluateAsync<double>("scrollY"));
    });

    [Fact]
    public Task A_press_on_the_area_leaves_focus_where_it_was() => RunAsync(async () =>
    {
        await OpenAsync();
        var intro = Page.Locator("[data-testid='ui-color-picker']");
        var input = intro.Locator("[data-ui-color-picker-popover-input]");

        await intro.Locator("[data-ui-color-picker-swatch-button]").ClickAsync();
        await input.FocusAsync();
        var area = await intro.Locator("[data-ui-color-picker-area]").BoundingBoxAsync();
        await Page.Mouse.ClickAsync(area!.X + (area.Width / 4), area.Y + (area.Height / 4));

        await Expect(intro.Locator("[data-ui-color-picker-area-thumb]")).ToHaveAttributeAsync("aria-valuetext", "Saturation 25%, brightness 75%");
        await Expect(input).ToBeFocusedAsync();
    });

    [Fact]
    public Task The_copy_button_puts_the_colour_as_it_is_written_on_the_clipboard() => RunAsync(async () =>
    {
        await OpenAsync();
        var copyable = Demo("copyable");
        var copy = copyable.Locator("[data-ui-color-picker-copy]");

        await copy.ClickAsync();

        await Expect(copy).ToHaveAttributeAsync("data-copied", "");
        Assert.Equal("#0ff1ce", await Page.EvaluateAsync<string>("navigator.clipboard.readText()"));
    });

    [Fact]
    public Task The_dropper_is_there_only_where_the_browser_has_one() => RunAsync(async () =>
    {
        await OpenAsync();
        var dropper = Demo("dropper");

        await dropper.Locator("[data-ui-color-picker-swatch-button]").ClickAsync();
        await Expect(dropper.Locator("dialog")).ToBeVisibleAsync();
        var has = await Page.EvaluateAsync<bool>("'EyeDropper' in window");

        await Expect(dropper.Locator("[data-ui-color-picker-dropper]")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Visible = has });
    });

    [Fact]
    public Task On_a_phone_the_popover_is_a_sheet_that_holds_the_colour_until_Select() => RunAsync(async () =>
    {
        // Reached at a desktop's width, where the sidebar is on the page, then narrowed: the picker learns the
        // width when its popover opens.
        await OpenAsync();
        await Page.SetViewportSizeAsync(390, 844);
        try
        {
            await Page.Locator("[data-testid='ui-color-picker']").ScrollIntoViewIfNeededAsync();
            var intro = Page.Locator("[data-testid='ui-color-picker']");
            var opener = intro.Locator("[data-ui-color-picker-swatch-button]");
            var dialog = intro.Locator("dialog");
            var input = intro.Locator("[data-ui-color-picker-input]");

            await opener.ClickAsync();
            await Expect(dialog).ToBeVisibleAsync();
            var sheet = (await dialog.BoundingBoxAsync())!;
            // Flux's sheet at 390: 336 wide, centred both ways, over a page that does not scroll.
            Assert.Equal(336, sheet.Width, 1);
            Assert.Equal((390 - sheet.Width) / 2, sheet.X, 1);
            Assert.Equal((844 - sheet.Height) / 2, sheet.Y, 1);
            Assert.Equal("hidden", await Page.EvaluateAsync<string>("getComputedStyle(document.documentElement).overflowY"));
            await Expect(intro.Locator("[data-ui-color-picker-sheet]")).ToHaveCSSAsync("width", "2px");
            await Page.Keyboard.PressAsync("ArrowRight");
            await Expect(intro.Locator("[data-ui-color-picker-popover-input]")).ToHaveValueAsync("#b9da51");
            await Expect(input).ToHaveValueAsync("#bada55");
            await Expect(State).ToContainTextAsync("Theme: #bada55.");
            await dialog.GetByText("Cancel").ClickAsync();
            await Expect(dialog).ToBeHiddenAsync();
            await Expect(input).ToHaveValueAsync("#bada55");
            await opener.ClickAsync();
            await Expect(intro.Locator("[data-ui-color-picker-popover-input]")).ToHaveValueAsync("#bada55");
            await Page.Keyboard.PressAsync("ArrowRight");
            await Expect(intro.Locator("[data-ui-color-picker-popover-input]")).ToHaveValueAsync("#b9da51");
            await dialog.GetByText("Select").ClickAsync();

            await Expect(dialog).ToBeHiddenAsync();
            await Expect(input).ToHaveValueAsync("#b9da51");
            await Expect(State).ToContainTextAsync("Theme: #b9da51.");
        }
        finally
        {
            await Page.SetViewportSizeAsync(1280, 720);
        }
    });

    [Fact]
    public Task On_a_phone_a_swatch_is_taken_at_once_and_closes_the_sheet() => RunAsync(async () =>
    {
        // Reached at a desktop's width, where the sidebar is on the page, then narrowed: the picker learns the
        // width when its popover opens.
        await OpenAsync();
        await Page.SetViewportSizeAsync(390, 844);
        try
        {
            await Page.Locator("[data-testid='ui-color-picker']").ScrollIntoViewIfNeededAsync();
            var intro = Page.Locator("[data-testid='ui-color-picker']");
            var dialog = intro.Locator("dialog");

            await intro.Locator("[data-ui-color-picker-swatch-button]").ClickAsync();
            await Expect(intro.Locator("[data-ui-color-picker-sheet]")).ToHaveCSSAsync("width", "2px");
            await intro.Locator("[data-ui-color-picker-swatch]").Nth(3).ClickAsync();

            await Expect(dialog).ToBeHiddenAsync();
            await Expect(intro.Locator("[data-ui-color-picker-input]")).ToHaveValueAsync("#eab308");
            await Expect(State).ToContainTextAsync("Theme: #eab308.");
        }
        finally
        {
            await Page.SetViewportSizeAsync(1280, 720);
        }
    });

    [Fact]
    public Task In_a_form_the_colour_is_saved_with_the_form_and_text_that_is_no_colour_holds_the_save() => RunAsync(async () =>
    {
        await OpenAsync();
        var form = Page.Locator("[data-testid='ui-color-picker-form']");
        var input = form.Locator("[data-ui-color-picker-input]");
        var saved = Page.Locator("[data-testid='ui-color-picker-saved']");
        await form.ScrollIntoViewIfNeededAsync();

        await input.FillAsync("#12");
        await form.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();
        await Expect(form.Locator("#ui-color-picker-accent-error")).ToHaveTextAsync("Enter a valid color.");
        await Expect(input).ToHaveValueAsync("#12");
        await Expect(saved).ToContainTextAsync("nothing yet");
        await input.FillAsync("#123");
        await form.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Expect(saved).ToContainTextAsync("Saved: #112233.");
        await Expect(input).ToHaveValueAsync("#112233");
        await Expect(form.Locator("#ui-color-picker-accent-error")).ToHaveTextAsync("");
    });

    private async Task OpenAsync()
    {
        await OpenUiPageAsync("color-picker", "Color picker");
        await Page.Locator("[data-testid='ui-color-picker']").ScrollIntoViewIfNeededAsync();
    }
}
