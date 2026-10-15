using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's color picker as the page draws it: the trigger, the popover, and the one field the colour reaches
///     the page through.
/// </summary>
/// <remarks>
///     Everything the reader does before a value is owed — the keys, a drag, typing, a swatch, the sheet — is the
///     browser's (<c>rask-color.ts</c>), and asserted in a browser (<c>RuntimeHookColorTests</c>). What reaches the
///     page is the value field's <c>change</c>, which is what these tests raise.
/// </remarks>
public partial class UiColorPickerTests : global::Rask.Core.RaskMarkup
{
    private const string Thumb = "[data-ui-color-picker-area-thumb]";
    private const string Swatch = "[data-ui-color-picker-swatch]";
    private const string TriggerInput = "[data-ui-color-picker-input]";
    private const string PopoverInput = "[data-ui-color-picker-popover-input]";
    private const string Hue = "[data-channel=\"hue\"] input[type=\"range\"]";
    private const string AreaField = "[data-ui-color-picker-area] > input[type=\"hidden\"]";
    private const string HueField = "[data-channel=\"hue\"] > input[type=\"hidden\"]";
    private const string ValueField = "[data-ui-color-picker] > input[type=\"hidden\"]";

    private string _color = "#bada55";

    private sealed class Theme
    {
        public string Accent { get; set; } = "#ef4444";
    }

    private UiColorPicker Picker() => Ui.ColorPicker.Value(_color).OnChange(color => _color = color);

    private static string Preview(Page page) => page.Find("[data-ui-color-picker-preview]").Attribute("style")!;

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
        Assert.Null(dialog.Attribute("data-rask-modal-open"));
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
        Assert.Equal(string.Empty, page.Find(AreaField).Attribute("value"));
        Assert.Equal("inset-inline-start:calc(var(--rask-drag-x, 0.6101) * 100%);top:calc(var(--rask-drag-y, 0.1451) * 100%)", thumb.Attribute("style"));
        Assert.Equal("Arrows Home End PageUp PageDown", thumb.Attribute("data-rask-contain-keys"));
        Assert.Equal("x", hue.Attribute("data-rask-drag"));
        Assert.Equal("8", hue.Attribute("data-rask-drag-inset"));
        Assert.Equal(page.Find(Hue).Attribute("id"), hue.Attribute("data-rask-focus"));
        Assert.Equal("Arrows", page.Find(Hue).Attribute("data-rask-contain-keys"));
        Assert.Equal(string.Empty, page.Find(HueField).Attribute("value"));
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
    public void The_browser_keeps_the_picker_by_its_format_its_words_and_its_one_field()
    {
        var page = Page.Render(Picker);

        var (root, field) = (page.Find("[data-ui-color-picker]"), page.Find(ValueField));

        Assert.Equal("hex", root.Attribute("data-rask-color"));
        Assert.Equal(page.Find("dialog").Attribute("id"), root.Attribute("data-rask-open-mark"));
        Assert.Equal(
            "{\"open\":\"Open color picker\",\"openWith\":\"Open color picker, {0}\",\"selected\":\"{0} selected\",\"area\":\"Saturation {0}%, brightness {1}%\"}",
            root.Attribute("data-rask-color-says"));
        Assert.Null(root.Attribute("data-rask-color-refuses"));
        Assert.Equal("#bada55", field.Attribute("value"));
        Assert.NotNull(field.Attribute("data-rask-on-change"));
        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Null(field.Attribute("data-rask-bind-on"));
    }

    [Fact]
    public void Nothing_the_reader_does_before_a_value_is_owed_has_a_handler()
    {
        var page = Page.Render(() => Picker().Clearable().Dropper());

        var handlers = System.Text.RegularExpressions.Regex.Matches(page.Html, "data-rask-on-[a-z]+=").Count;

        Assert.Equal(1, handlers);
        Assert.NotNull(page.Find(ValueField).Attribute("data-rask-on-change"));
    }

    [Fact]
    public async Task A_colour_the_browser_announces_reaches_the_page_and_is_drawn()
    {
        var page = Page.Render(Picker);

        await page.On(ValueField).Change("#14b8a6");

        Assert.Equal("#14b8a6", _color);
        Assert.Equal("#14b8a6", page.Find(TriggerInput).Attribute("value"));
        Assert.Equal("#14b8a6", page.Find(PopoverInput).Attribute("value"));
        Assert.Equal("Open color picker, Teal", page.Find("[data-ui-color-picker-swatch-button]").Attribute("aria-label"));
        Assert.Equal("Teal", page.Find(Swatch + "[aria-selected=\"true\"]").Attribute("aria-label"));
        Assert.Contains("background-color:rgb(20 184 166)", Preview(page), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_that_is_no_colour_is_put_back_where_no_form_could_say_so()
    {
        var page = Page.Render(Picker);

        await page.On(ValueField).Change("zzz");

        Assert.Equal("#bada55", _color);
        Assert.Equal("#bada55", page.Find(TriggerInput).Attribute("value"));
    }

    [Fact]
    public async Task An_emptied_value_clears_the_colour()
    {
        var page = Page.Render(Picker);

        await page.On(ValueField).Change("");

        Assert.Equal(string.Empty, _color);
        Assert.Contains("background-color:transparent", Preview(page), StringComparison.Ordinal);
        Assert.Equal("Open color picker", page.Find("[data-ui-color-picker-swatch-button]").Attribute("aria-label"));
        Assert.Equal("Saturation 0%, brightness 0%", page.Find(Thumb).Attribute("aria-valuetext"));
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

        var before = page.Find("[aria-label=\"Clear color\"]").Attribute("hidden");
        await page.On(ValueField).Change("");

        Assert.Null(before);
        Assert.Equal("clear", page.Find("[aria-label=\"Clear color\"]").Attribute("data-rask-color-part"));
        Assert.Equal(string.Empty, _color);
        Assert.NotNull(page.Find("[aria-label=\"Clear color\"]").Attribute("hidden"));
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
    public async Task Children_compose_the_popover_and_its_parts_draw_the_same_colour()
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

        await page.On(ValueField).Change("#0000ff");

        Assert.Equal("#0000ff", _color);
        Assert.Equal("Blue", page.Find(Swatch + "[aria-selected=\"true\"]").Attribute("aria-label"));
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
        Assert.Equal("dropper", dropper.Attribute("data-rask-color-part"));
    }

    [Fact]
    public void A_disabled_picker_opens_nothing_and_moves_nothing()
    {
        var page = Page.Render(() => Picker().Disabled());

        var root = page.Find("[data-ui-color-picker]");

        Assert.NotNull(page.Find("[data-ui-color-picker-swatch-button]").Attribute("disabled"));
        Assert.NotNull(page.Find(TriggerInput).Attribute("disabled"));
        Assert.Null(root.Attribute("data-rask-color"));
        Assert.Null(page.Find("[data-ui-color-picker-area]").Attribute("data-rask-drag"));
        Assert.Null(page.Find(ValueField).Attribute("data-rask-on-change"));
    }

    [Fact]
    public async Task A_bound_picker_writes_the_member_with_the_next_action_and_a_label_draws_the_field()
    {
        var theme = new Theme();
        var page = Acting.Render(() => Ui.ColorPicker.Bind(() => theme.Accent).Label("Accent").Name("accent"));

        var waits = page.Find(ValueField).Attribute("data-rask-bind-on");
        await page.On(ValueField).Change("#22c55e");
        var (waiting, shown) = (theme.Accent, page.Find(TriggerInput).Attribute("value"));
        await Acting.Act(page);

        Assert.Equal("action", waits);
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

        var field = page.Find(ValueField);
        await page.On(ValueField).Input("#22c55e");

        Assert.NotNull(field.Attribute("data-rask-on-input"));
        Assert.Null(field.Attribute("data-rask-bind-on"));
        Assert.Equal("#22c55e", theme.Accent);
    }

    [Fact]
    public async Task In_a_form_text_that_is_no_colour_stays_in_the_field_says_so_and_holds_the_save()
    {
        var theme = new Theme();
        var saves = 0;
        var page = Acting.Render(() => Form.Model(theme).OnSubmit(_ => saves++)[Ui.ColorPicker.Bind(() => theme.Accent).Label("Accent").Id("accent")]);

        var refuses = page.Find("[data-ui-color-picker]").Attribute("data-rask-color-refuses");
        await page.On(ValueField).Change("#12");
        await page.On("form").Submit();

        Assert.NotNull(refuses);
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
        await page.On(ValueField).Change("#12");
        await page.On("form").Submit();

        await page.On(ValueField).Change("#112233");
        await page.On("form").Submit();

        Assert.Equal("", page.TextOf("#accent-error"));
        Assert.Equal("#112233", page.Find(TriggerInput).Attribute("value"));
        Assert.Equal(1, saves);
        Assert.Equal("#112233", theme.Accent);
    }

    [Fact]
    public void A_picker_with_neither_Bind_nor_OnChange_keeps_the_colour_in_the_browser()
    {
        var page = Page.Render(() => Ui.ColorPicker.Value("#bada55").Name("accent"));

        var field = page.Find(ValueField);

        Assert.Equal("accent", field.Attribute("name"));
        Assert.Equal("#bada55", field.Attribute("value"));
        Assert.Null(field.Attribute("data-rask-on-change"));
        Assert.Null(field.Attribute("data-rask-on-input"));
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
