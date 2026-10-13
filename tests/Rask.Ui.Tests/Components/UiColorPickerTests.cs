using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's color picker: the trigger, the popover it opens, and the keyboard as it was recorded on
///     fluxui.dev — the area by 2 and by 10, a typed colour on Enter or on leaving, a swatch at a press.
/// </summary>
/// <remarks>
///     The browser owns whether the dialog is open and says so through the <c>toggle</c> event, and the pointer
///     is the runtime's: what reaches the page is the bound field of the surface it dragged across, which is
///     what these tests raise. The drag itself is the browser tests' (UiKitColorPickerTests).
/// </remarks>
public partial class UiColorPickerTests : global::Rask.Core.RaskMarkup
{
    private const string Opened = "{\"oldState\":\"closed\",\"newState\":\"open\"}";
    private const string Closed = "{\"oldState\":\"open\",\"newState\":\"closed\"}";
    private const string Thumb = "[data-ui-color-picker-area-thumb]";
    private const string Swatch = "[data-ui-color-picker-swatch]";
    private const string TriggerInput = "[data-ui-color-picker-input]";
    private const string PopoverInput = "[data-ui-color-picker-popover-input]";
    private const string Hue = "[data-channel=\"hue\"] input[type=\"range\"]";
    private const string AreaField = "[data-ui-color-picker-area] > input[type=\"hidden\"]";
    private const string HueField = "[data-channel=\"hue\"] > input[type=\"hidden\"]";
    private const string Probe = "[data-ui-color-picker-sheet] input";
    private const string Select = "dialog [role=\"button\"]";

    private string _color = "#bada55";

    private sealed class Theme
    {
        public string Accent { get; set; } = "#ef4444";
    }

    private UiColorPicker Picker() => Ui.ColorPicker.Value(_color).OnChange(color => _color = color);

    private static Task Key(Page page, string selector, string key, bool shift = false) =>
        page.On(selector).Raise("keydown", $"{{\"key\":\"{key}\",\"shiftKey\":{(shift ? "true" : "false")}}}");

    private static string Preview(Page page) => page.Find("[data-ui-color-picker-preview]").Attribute("style")!;

    // The popover as Flux's sheet: open, and measured two pixels wide by the stylesheet's probe.
    private static async Task OnTheSheet(Page page)
    {
        await page.On("dialog").Raise("toggle", Opened);
        await page.On(Probe).Input("2 1");
    }

    [Fact]
    public void The_trigger_is_a_swatch_button_and_a_text_input_over_a_dialog()
    {
        var page = Page.Render(Picker);

        var (button, input, dialog) = (page.Find("[data-ui-color-picker-swatch-button]"), page.Find(TriggerInput), page.Find("dialog"));

        Assert.Equal("Open color picker, #bada55", button.Attribute("aria-label"));
        Assert.Equal("dialog", button.Attribute("aria-haspopup"));
        Assert.Equal("false", button.Attribute("aria-expanded"));
        Assert.Equal("show-modal", button.Attribute("command"));
        Assert.Equal(dialog.Attribute("id"), button.Attribute("commandfor"));
        Assert.Equal(dialog.Attribute("id"), button.Attribute("aria-controls"));
        Assert.Equal("combobox", input.Attribute("role"));
        Assert.Equal("Color", input.Attribute("aria-label"));
        Assert.Equal("none", input.Attribute("aria-autocomplete"));
        Assert.Equal("#bada55", input.Attribute("value"));
        Assert.Equal("#000000", input.Attribute("placeholder"));
        Assert.Equal("any", dialog.Attribute("data-rask-modal"));
        Assert.Equal("false", dialog.Attribute("data-rask-modal-open"));
        Assert.Contains("background-color:rgb(186 218 85)", Preview(page), StringComparison.Ordinal);
    }

    [Fact]
    public void The_popover_holds_the_area_the_hue_slider_the_input_and_24_swatches()
    {
        var page = Page.Render(Picker);

        var thumb = page.Find(Thumb);

        Assert.Equal("slider", thumb.Attribute("role"));
        Assert.Equal("Saturation and brightness", thumb.Attribute("aria-label"));
        Assert.Equal("2D color picker", thumb.Attribute("aria-roledescription"));
        Assert.Equal("61", thumb.Attribute("aria-valuenow"));
        Assert.Equal("Saturation 61%, brightness 85%", thumb.Attribute("aria-valuetext"));
        Assert.NotNull(thumb.Attribute("autofocus"));
        Assert.Equal("74", page.Find(Hue).Attribute("value"));
        Assert.Equal("Hue", page.Find(Hue).Attribute("aria-label"));
        Assert.False(page.Exists("[data-channel=\"alpha\"]"));
        Assert.Equal("Color value", page.Find(PopoverInput).Attribute("aria-label"));
        Assert.Equal("listbox", page.Find("[data-ui-color-picker-swatches]").Attribute("role"));
        Assert.Equal(24, page.FindAll(Swatch + "[role=\"option\"]").Count);
        Assert.Equal(["Cancel", "Select"], page.FindAll("dialog [data-ui-button]").Select(button => button.TextContent.Trim()));
    }

    [Fact]
    public void The_surfaces_ask_the_runtime_for_the_pointer_the_keys_and_the_focus()
    {
        var page = Page.Render(Picker);

        var (area, thumb, hue) = (page.Find("[data-ui-color-picker-area]"), page.Find(Thumb), page.Find("[data-channel=\"hue\"]"));

        Assert.Equal("x y", area.Attribute("data-rask-drag"));
        Assert.NotNull(area.Attribute("data-rask-press-keeps-focus"));
        Assert.Equal("0.6101 0.1451", page.Find(AreaField).Attribute("value"));
        Assert.Equal("inset-inline-start:calc(var(--rask-drag-x, 0.6101) * 100%);top:calc(var(--rask-drag-y, 0.1451) * 100%)", thumb.Attribute("style"));
        Assert.Equal("Arrows Home End PageUp PageDown", thumb.Attribute("data-rask-contain-keys"));
        Assert.Equal("x", hue.Attribute("data-rask-drag"));
        Assert.Equal("8", hue.Attribute("data-rask-drag-inset"));
        Assert.Equal(page.Find(Hue).Attribute("id"), hue.Attribute("data-rask-focus"));
        Assert.Equal("Arrows", page.Find(Hue).Attribute("data-rask-contain-keys"));
        Assert.Equal("0.2056", page.Find(HueField).Attribute("value"));
        Assert.Equal("Arrows Home End PageUp PageDown Space Enter", page.Find("[data-ui-color-picker-swatches]").Attribute("data-rask-contain-keys"));
    }

    [Fact]
    public void An_alpha_format_adds_the_opacity_slider_and_writes_the_alpha()
    {
        var page = Page.Render(() => Ui.ColorPicker.Value("#6366f180").Format(Ui.ColorPickerFormat.Rgba));

        var alpha = page.Find("[data-channel=\"alpha\"] input[type=\"range\"]");

        Assert.Equal("50", alpha.Attribute("value"));
        Assert.Equal("Opacity", alpha.Attribute("aria-label"));
        Assert.Equal("rgb(99 102 241 / 0.5)", page.Find(TriggerInput).Attribute("value"));
        Assert.Equal("Enter a color", page.Find(TriggerInput).Attribute("placeholder"));
        Assert.Equal("Open color picker, Indigo", page.Find("[data-ui-color-picker-swatch-button]").Attribute("aria-label"));
    }

    [Fact]
    public async Task The_dialog_says_when_it_opened_and_closed()
    {
        var page = Page.Render(Picker);

        await page.On("dialog").Raise("toggle", Opened);
        var open = page.Find(TriggerInput).Attribute("aria-expanded");
        var marked = page.Find("[data-ui-color-picker]").Attribute("data-open");
        await page.On("dialog").Raise("toggle", Closed);

        Assert.Equal("true", open);
        Assert.NotNull(marked);
        Assert.Equal("false", page.Find(TriggerInput).Attribute("aria-expanded"));
        Assert.Equal("false", page.Find("[data-ui-color-picker-swatch-button]").Attribute("aria-expanded"));
        Assert.Null(page.Find("[data-ui-color-picker]").Attribute("data-open"));
    }

    [Fact]
    public async Task ArrowDown_in_the_trigger_asks_the_runtime_to_open_the_popover()
    {
        var page = Page.Render(Picker);

        await Key(page, TriggerInput, "ArrowDown");

        Assert.Equal("true", page.Find(TriggerInput).Attribute("aria-expanded"));
        Assert.Equal("true", page.Find("dialog").Attribute("data-rask-modal-open"));
    }

    [Fact]
    public async Task The_arrows_move_the_area_by_two_and_by_ten_with_shift()
    {
        var page = Page.Render(Picker);

        await Key(page, Thumb, "ArrowRight");
        var right = _color;
        await Key(page, Thumb, "ArrowDown");
        var down = _color;
        await Key(page, Thumb, "ArrowLeft", shift: true);
        var far = _color;
        await Key(page, Thumb, "ArrowUp", shift: true);

        Assert.Equal("#b9da51", right);
        Assert.Equal("#b5d54f", down);
        Assert.Equal("#bad564", far);
        Assert.Equal("#d0ee70", _color);
        Assert.Equal("0.5301 0.0651", page.Find(AreaField).Attribute("value"));
    }

    [Fact]
    public async Task The_page_keys_and_Home_and_End_go_to_the_edges_of_the_area()
    {
        var page = Page.Render(Picker);

        await Key(page, Thumb, "PageUp");
        var top = _color;
        await Key(page, Thumb, "End");
        var corner = _color;
        await Key(page, Thumb, "Home");
        var white = _color;
        await Key(page, Thumb, "PageDown");

        Assert.Equal("#daff63", top);
        Assert.Equal("#c2ff00", corner);
        Assert.Equal("#ffffff", white);
        Assert.Equal("#000000", _color);
    }

    [Fact]
    public async Task Black_keeps_the_hue_and_names_its_swatch()
    {
        var page = Page.Render(Picker);

        await Key(page, Thumb, "PageDown");

        Assert.Equal("#000000", _color);
        Assert.Equal("74", page.Find(Hue).Attribute("value"));
        Assert.Equal("Open color picker, Black", page.Find("[data-ui-color-picker-swatch-button]").Attribute("aria-label"));
        Assert.Equal("Black", page.Find(Swatch + "[aria-selected=\"true\"]").Attribute("aria-label"));
        Assert.Equal("Black", page.Find(Swatch + "[tabindex=\"0\"]").Attribute("aria-label"));
    }

    [Fact]
    public async Task The_pointer_on_the_area_is_saturation_across_and_brightness_up()
    {
        var page = Page.Render(Picker);

        await page.On(AreaField).Input("0.5 0.25");

        Assert.Equal("#a8bf60", _color);
        Assert.Equal("Saturation 50%, brightness 75%", page.Find(Thumb).Attribute("aria-valuetext"));
        Assert.Equal("0.5 0.25", page.Find(AreaField).Attribute("value"));
    }

    [Fact]
    public async Task The_pointer_on_a_track_moves_its_channel_in_whole_steps()
    {
        var page = Page.Render(Picker);

        await page.On(HueField).Input("0.4991");

        Assert.Equal("#55dada", _color);
        Assert.Equal("180", page.Find(Hue).Attribute("value"));
        Assert.Equal("0.5", page.Find(HueField).Attribute("value"));
        Assert.Contains("background-color:rgb(0 255 255)", page.Find("[data-ui-color-picker-area-hue]").Attribute("style"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_arrow_steps_a_slider_by_one_and_by_ten_with_shift()
    {
        var page = Page.Render(Picker);

        await Key(page, Hue, "ArrowRight");
        var one = page.Find(Hue).Attribute("value");
        await Key(page, Hue, "ArrowLeft", shift: true);

        Assert.Equal("75", one);
        Assert.Equal("65", page.Find(Hue).Attribute("value"));
        Assert.Equal("#cfda55", _color);
    }

    [Fact]
    public async Task The_hue_slider_takes_the_range_inputs_value()
    {
        var page = Page.Render(Picker);

        await page.On(Hue).Change("180");

        Assert.Equal("#55dada", _color);
        Assert.Equal("180", page.Find(Hue).Attribute("value"));
    }

    [Fact]
    public async Task A_typed_colour_is_taken_in_any_css_form_and_written_in_the_pickers_format()
    {
        var page = Page.Render(Picker);

        await page.On(TriggerInput).Change("#f00");
        var red = _color;
        await page.On(PopoverInput).Change("hsl(120 50% 50%)");
        var green = _color;
        await page.On(TriggerInput).Change("oklch(0.7 0.1 200)");

        Assert.Equal("#ff0000", red);
        Assert.Equal("#40bf40", green);
        Assert.Equal("#0100c8", _color);
        Assert.Equal("#0100c8", page.Find(TriggerInput).Attribute("value"));
        Assert.Equal("#0100c8", page.Find(PopoverInput).Attribute("value"));
    }

    [Fact]
    public async Task Text_that_is_no_colour_is_put_back_where_no_form_could_say_so()
    {
        var page = Page.Render(Picker);

        await page.On(TriggerInput).Change("zzz");

        Assert.Equal("#bada55", _color);
        Assert.Equal("#bada55", page.Find(TriggerInput).Attribute("value"));
    }

    [Fact]
    public async Task An_emptied_input_clears_the_colour()
    {
        var page = Page.Render(Picker);

        await page.On(TriggerInput).Change("");

        Assert.Equal(string.Empty, _color);
        Assert.Contains("background-color:transparent", Preview(page), StringComparison.Ordinal);
        Assert.Equal("Open color picker", page.Find("[data-ui-color-picker-swatch-button]").Attribute("aria-label"));
        Assert.Equal("Saturation 0%, brightness 0%", page.Find(Thumb).Attribute("aria-valuetext"));
    }

    [Fact]
    public async Task A_typed_alpha_stays_with_the_colour_though_hex_does_not_write_it()
    {
        var page = Page.Render(Picker);

        await page.On(TriggerInput).Change("rgb(64 191 64 / 0.5)");

        Assert.Equal("#40bf40", _color);
        Assert.Contains("background-color:rgb(64 191 64 / 0.5)", Preview(page), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Picking_a_swatch_sets_the_colour_and_announces_it()
    {
        var page = Page.Render(Picker);

        await page.On(Swatch + "[aria-label=\"Teal\"]").Click();

        Assert.Equal("#14b8a6", _color);
        Assert.Equal("Teal", page.Find(Swatch + "[aria-selected=\"true\"]").Attribute("aria-label"));
        Assert.NotNull(page.Find(Swatch + "[aria-label=\"Teal\"]").Attribute("data-selected"));
        Assert.Equal("Teal selected", page.TextOf("[data-ui-color-picker-announce]"));
        // Flux leaves the tab stop where it was until the popover opens again.
        Assert.Equal("Red", page.Find(Swatch + "[tabindex=\"0\"]").Attribute("aria-label"));
    }

    [Fact]
    public async Task The_tab_stop_follows_the_picked_swatch_when_the_popover_opens_again()
    {
        var page = Page.Render(Picker);
        await page.On("dialog").Raise("toggle", Opened);
        await page.On(Swatch + "[aria-label=\"Teal\"]").Click();

        await page.On("dialog").Raise("toggle", Closed);
        await page.On("dialog").Raise("toggle", Opened);

        Assert.Equal("Teal", page.Find(Swatch + "[tabindex=\"0\"]").Attribute("aria-label"));
    }

    [Fact]
    public async Task The_arrows_walk_the_swatches_eight_to_a_row_and_stop_at_the_edges()
    {
        var page = Page.Render(Picker);
        string Stop() => page.Find(Swatch + "[tabindex=\"0\"]").Attribute("aria-label")!;

        await Key(page, Swatch + "[aria-label=\"Red\"]", "ArrowDown");
        var down = Stop();
        await Key(page, Swatch + "[aria-label=\"Purple\"]", "ArrowRight");
        var right = Stop();
        await Key(page, Swatch + "[aria-label=\"Pink\"]", "End");
        var end = Stop();
        await Key(page, Swatch + "[aria-label=\"White\"]", "ArrowRight");
        var edge = Stop();
        await Key(page, Swatch + "[aria-label=\"White\"]", "ArrowDown");
        var bottom = Stop();
        await Key(page, Swatch + "[aria-label=\"White\"]", "Home");

        Assert.Equal(["Purple", "Pink", "White", "White", "White", "Red"], [down, right, end, edge, bottom, Stop()]);
        Assert.Equal("#bada55", _color);
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    public async Task Enter_and_Space_pick_the_swatch_they_are_pressed_on(string key)
    {
        var page = Page.Render(Picker);

        await Key(page, Swatch + "[aria-label=\"Sky\"]", key);

        Assert.Equal("#0ea5e9", _color);
    }

    [Fact]
    public async Task On_the_sheet_a_change_waits_for_Select()
    {
        var page = Page.Render(Picker);
        await OnTheSheet(page);

        await Key(page, Thumb, "ArrowRight");
        await page.On(HueField).Input("0.5");
        var (held, trigger, inside) = (_color, page.Find(TriggerInput).Attribute("value"), page.Find(PopoverInput).Attribute("value"));
        await page.On(Select).Click();

        Assert.Equal("#bada55", held);
        Assert.Equal("#bada55", trigger);
        Assert.Equal("#51dada", inside);
        Assert.Equal("#51dada", _color);
        Assert.Equal("#51dada", page.Find(TriggerInput).Attribute("value"));
        Assert.Equal("10", page.Find("[data-channel=\"hue\"]").Attribute("data-rask-drag-inset"));
    }

    [Fact]
    public async Task On_the_sheet_a_typed_colour_is_held_and_its_input_says_the_pages_again_as_fluxs_does()
    {
        var page = Page.Render(Picker);
        await OnTheSheet(page);

        await page.On(PopoverInput).Change("#00ff00");
        var (held, inside, area) = (_color, page.Find(PopoverInput).Attribute("value"), page.Find(AreaField).Attribute("value"));
        await page.On(Select).Click();

        Assert.Equal("#bada55", held);
        Assert.Equal("#bada55", inside);
        Assert.Equal("1 0", area);
        Assert.Equal("#00ff00", _color);
    }

    [Fact]
    public async Task On_the_sheet_what_nobody_selected_is_gone_when_it_closes()
    {
        var page = Page.Render(Picker);
        await OnTheSheet(page);
        await Key(page, Thumb, "ArrowRight");

        await page.On("dialog").Raise("toggle", Closed);

        Assert.Equal("#bada55", _color);
        Assert.Equal("#bada55", page.Find(PopoverInput).Attribute("value"));
        Assert.Equal("0.6101 0.1451", page.Find(AreaField).Attribute("value"));
    }

    [Fact]
    public async Task On_the_sheet_a_swatch_is_taken_at_once_and_closes_it()
    {
        var page = Page.Render(Picker);
        await OnTheSheet(page);

        await page.On(Swatch + "[aria-label=\"Yellow\"]").Click();

        Assert.Equal("#eab308", _color);
        Assert.Equal("false", page.Find("dialog").Attribute("data-rask-modal-open"));
        Assert.Equal("Yellow selected", page.TextOf("[data-ui-color-picker-announce]"));
        Assert.Equal("Yellow", page.Find(Swatch + "[tabindex=\"0\"]").Attribute("aria-label"));
    }

    [Fact]
    public async Task Under_a_pointer_Select_has_nothing_left_to_take()
    {
        var page = Page.Render(Picker);
        await page.On("dialog").Raise("toggle", Opened);
        await Key(page, Thumb, "ArrowRight");
        var live = _color;

        await page.On(Select).Click();

        Assert.Equal("#b9da51", live);
        Assert.Equal("#b9da51", _color);
    }

    [Fact]
    public void The_button_trigger_shows_the_value_and_opens_by_command()
    {
        var page = Page.Render(() => Ui.ColorPicker.Value("#ca1ebb").Type(Ui.ColorPickerType.Button));

        var button = page.Find("[data-ui-color-picker-button]");

        Assert.Equal("button", button.Tag);
        Assert.Equal("show-modal", button.Attribute("command"));
        Assert.Equal("dialog", button.Attribute("aria-haspopup"));
        Assert.Equal("#ca1ebb", page.TextOf("[data-ui-color-picker-value]"));
        Assert.Equal("#000000", page.TextOf("[data-ui-color-picker-placeholder]"));
        Assert.False(page.Exists(TriggerInput));
    }

    [Fact]
    public async Task Clearable_draws_a_clear_button_that_is_out_of_sight_while_there_is_no_colour()
    {
        var page = Page.Render(() => Picker().Clearable());

        var before = page.Find("[aria-label=\"Clear color\"]").Attribute("class")!;
        await page.On("[aria-label=\"Clear color\"]").Click();

        Assert.DoesNotContain("hidden", before, StringComparison.Ordinal);
        Assert.Equal(string.Empty, _color);
        Assert.Contains("hidden", page.Find("[aria-label=\"Clear color\"]").Attribute("class"), StringComparison.Ordinal);
    }

    [Fact]
    public void Copyable_draws_a_button_the_runtime_copies_the_inputs_text_from()
    {
        var page = Page.Render(() => Picker().Copyable());

        var copy = page.Find("[data-ui-color-picker-copy]");

        Assert.Equal("Copy to clipboard", copy.Attribute("aria-label"));
        Assert.Equal(page.Find(TriggerInput).Attribute("id"), copy.Attribute("data-rask-copy"));
        Assert.Equal("-1", copy.Attribute("tabindex"));
        Assert.Equal(2, page.FindAll("[data-ui-color-picker-copy] svg").Count);
    }

    [Fact]
    public void Swatches_replaces_the_palette_and_an_empty_list_hides_it()
    {
        var own = Page.Render(() => Ui.ColorPicker.Value("#22c55e").Swatches([("#ef4444", "Red"), "#22c55e"]));
        var none = Page.Render(() => Ui.ColorPicker.Value("#22c55e").Swatches([]));

        var labels = own.FindAll(Swatch).Select(swatch => swatch.Attribute("aria-label"));

        Assert.Equal(["Red", "#22c55e"], labels);
        Assert.Equal("#22c55e", own.Find(Swatch + "[aria-selected=\"true\"]").Attribute("data-value"));
        Assert.False(none.Exists("[data-ui-color-picker-swatches]"));
    }

    [Fact]
    public async Task Children_compose_the_popover_and_its_parts_move_the_same_colour()
    {
        var page = Page.Render(() => Picker()[
            Div.Class("flex flex-col gap-3")[
                Ui.ColorPickerInput.Placeholder("Any colour"),
                Ui.ColorPickerArea.Class("my-area"),
                Ui.ColorPickerSlider.Channel(Ui.ColorPickerChannel.Alpha),
                Ui.ColorPickerSwatches[Ui.ColorPickerSwatch.Value("#0000ff").Label("Blue")],
                Ui.ColorPickerDropper
            ]
        ]);

        await Key(page, Thumb, "ArrowRight");
        var moved = _color;
        await page.On(Swatch).Click();

        Assert.Equal("#b9da51", moved);
        Assert.Equal("#0000ff", _color);
        Assert.Equal("#0000ff", page.Find(TriggerInput).Attribute("value"));
        Assert.Equal("Any colour", page.Find(PopoverInput).Attribute("placeholder"));
        Assert.Contains("my-area", page.Find("[data-ui-color-picker-area]").Attribute("class"), StringComparison.Ordinal);
        Assert.True(page.Exists("[data-channel=\"alpha\"]"));
        Assert.False(page.Exists("[data-channel=\"hue\"]"));
        Assert.Empty(page.FindAll("dialog [data-ui-button]"));
    }

    [Fact]
    public void The_dropper_is_hidden_until_the_runtime_finds_the_browser_has_one()
    {
        var page = Page.Render(() => Picker().Dropper());

        var dropper = page.Find("[data-ui-color-picker-dropper]");

        Assert.Equal("Pick a color from the screen", dropper.Attribute("aria-label"));
        Assert.NotNull(dropper.Attribute("hidden"));
        Assert.Equal("EyeDropper", dropper.Attribute("data-rask-requires"));
    }

    [Fact]
    public async Task A_disabled_picker_opens_nothing_and_moves_nothing()
    {
        var page = Page.Render(() => Picker().Disabled());

        await Key(page, TriggerInput, "ArrowDown");
        await Key(page, Thumb, "ArrowRight");

        Assert.NotNull(page.Find("[data-ui-color-picker-swatch-button]").Attribute("disabled"));
        Assert.NotNull(page.Find(TriggerInput).Attribute("disabled"));
        Assert.Equal("false", page.Find(TriggerInput).Attribute("aria-expanded"));
        Assert.Null(page.Find("[data-ui-color-picker-area]").Attribute("data-rask-drag"));
        Assert.Equal("#bada55", _color);
    }

    [Fact]
    public async Task A_bound_picker_writes_the_member_with_the_next_action_and_a_label_draws_the_field()
    {
        var theme = new Theme();
        var page = Acting.Render(() => Ui.ColorPicker.Bind(() => theme.Accent).Label("Accent").Name("accent"));

        await page.On(Swatch + "[aria-label=\"Green\"]").Click();
        var (waiting, shown) = (theme.Accent, page.Find(TriggerInput).Attribute("value"));
        await Acting.Act(page);

        Assert.Equal("#ef4444", waiting);
        Assert.Equal("#22c55e", shown);
        Assert.Equal("#22c55e", theme.Accent);
        Assert.Equal("Accent", page.TextOf("label"));
        Assert.Equal(page.Find(TriggerInput).Attribute("id"), page.Find("label").Attribute("for"));
        Assert.Equal(page.Find("label").Attribute("id"), page.Find(TriggerInput).Attribute("aria-labelledby"));
        Assert.Equal("#22c55e", page.Find("input[type=\"hidden\"][name=\"accent\"]").Attribute("value"));
    }

    [Fact]
    public async Task A_live_picker_writes_the_member_as_the_colour_is_chosen()
    {
        var theme = new Theme();
        var page = Page.Render(() => Ui.ColorPicker.Bind(() => theme.Accent).Live());

        await page.On(Swatch + "[aria-label=\"Green\"]").Click();

        Assert.Equal("#22c55e", theme.Accent);
    }

    [Fact]
    public async Task In_a_form_text_that_is_no_colour_stays_in_the_field_says_so_and_holds_the_save()
    {
        var theme = new Theme();
        var saves = 0;
        var page = Acting.Render(() => Form.Model(theme).OnSubmit(_ => saves++)[Ui.ColorPicker.Bind(() => theme.Accent).Label("Accent").Id("accent")]);

        await page.On(TriggerInput).Change("#12");
        await page.On("form").Submit();

        Assert.Equal("Enter a valid color.", page.TextOf("#accent-error"));
        Assert.Equal("#12", page.Find(TriggerInput).Attribute("value"));
        Assert.Equal("true", page.Find(TriggerInput).Attribute("aria-invalid"));
        Assert.Equal(0, saves);
        Assert.Equal("#ef4444", theme.Accent);
    }

    [Fact]
    public async Task A_colour_typed_after_the_refused_text_takes_the_sentence_away_and_saves()
    {
        var theme = new Theme();
        var saves = 0;
        var page = Acting.Render(() => Form.Model(theme).OnSubmit(_ => saves++)[Ui.ColorPicker.Bind(() => theme.Accent).Label("Accent").Id("accent")]);
        await page.On(TriggerInput).Change("#12");
        await page.On("form").Submit();

        await page.On(TriggerInput).Change("#123");
        await page.On("form").Submit();

        Assert.Equal("", page.TextOf("#accent-error"));
        Assert.Equal("#112233", page.Find(TriggerInput).Attribute("value"));
        Assert.Equal(1, saves);
        Assert.Equal("#112233", theme.Accent);
    }

    [Fact]
    public async Task A_picker_with_neither_Bind_nor_OnChange_keeps_the_colour_itself()
    {
        var page = Page.Render(() => Ui.ColorPicker.Value("#bada55"));

        await Key(page, Thumb, "ArrowRight");

        Assert.Equal("#b9da51", page.Find(TriggerInput).Attribute("value"));
    }

    [Fact]
    public void A_hungarian_reader_hears_the_picker_in_hungarian()
    {
        var html = KitCulture.In("hu-HU", () => Ui.ColorPicker.Value("#ef4444").Dropper().Clearable().ToHtml()).AsText();

        Assert.Contains("aria-label=\"Színválasztó megnyitása, Piros\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Telítettség és fényerő\"", html, StringComparison.Ordinal);
        Assert.Contains("Mégse", html, StringComparison.Ordinal);
        Assert.Contains("Kiválasztás", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Open color picker", html, StringComparison.Ordinal);
    }
}
