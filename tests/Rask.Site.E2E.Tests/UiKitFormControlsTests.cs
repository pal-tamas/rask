using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's basic form controls (field, input, textarea, checkbox, radio, switch, slider and OTP input) on their
///     pages in the UI tab, in a browser: what the runtime and the browser do that no markup assertion can see.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitFormControlsTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Theory]
    [InlineData("field", "Field", "ui-field|ui-field-shorthand|ui-field-trailing|ui-field-badge|ui-field-split|ui-field-fieldset|ui-bound")]
    [InlineData("input", "Input", "ui-input|ui-input-shorthand|ui-input-class|ui-input-types|ui-input-file|ui-input-sm|ui-input-disabled|ui-input-readonly|ui-input-invalid|ui-input-mask|ui-input-icons|ui-input-icon-buttons|ui-input-clearable|ui-input-kbd|ui-input-button|ui-input-group|ui-input-affixes|ui-input-group-label")]
    [InlineData("textarea", "Textarea", "ui-textarea-bare|ui-textarea|ui-textarea-rows|ui-textarea-auto|ui-textarea-resize")]
    [InlineData("checkbox", "Checkbox", "ui-checkbox|ui-checkbox-group|ui-checkbox-descriptions|ui-checkbox-fieldset|ui-checkbox-all|ui-checkbox-checked|ui-checkbox-disabled|ui-checkbox-cards|ui-checkbox-vertical|ui-checkbox-card-icons|ui-checkbox-custom|ui-checkbox-pills|ui-checkbox-buttons")]
    [InlineData("radio", "Radio", "ui-radio|ui-radio-descriptions|ui-radio-fieldset|ui-radio-segmented|ui-radio-segmented-sm|ui-radio-segmented-icons|ui-radio-cards|ui-radio-vertical|ui-radio-card-icons|ui-radio-bare|ui-radio-custom|ui-radio-pills|ui-radio-buttons")]
    [InlineData("switch", "Switch", "ui-switch|ui-switch-fieldset|ui-switch-left")]
    [InlineData("slider", "Slider", "ui-slider|ui-slider-step|ui-slider-value|ui-slider-input|ui-slider-big|ui-slider-marks|ui-slider-dots|ui-slider-numbers|ui-slider-ticks|ui-slider-range|ui-slider-apart|ui-slider-prices|ui-slider-styles")]
    [InlineData("otp-input", "OTP input", "ui-otp|ui-otp-form|ui-otp-auto|ui-otp-alphanumeric|ui-otp-private|ui-otp-separator|ui-otp-group|ui-otp-groups")]
    public Task Every_form_control_example_renders_with_a_real_size(string slug, string heading, string testids) => RunAsync(async () =>
    {
        await OpenUiPageAsync(slug, heading);

        foreach (var id in testids.Split('|'))
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height.");
        }
    });

    [Fact]
    public Task Typing_into_a_field_reaches_CSharp_and_comes_back() => RunAsync(async () =>
    {
        await OpenUiPageAsync("field", "Field");
        var email = Page.Locator("[data-testid='ui-field'] input[type='email']");
        await Expect(email).ToHaveAccessibleNameAsync("Email");

        await email.FillAsync("not-an-address");
        await email.BlurAsync();

        // The error only shows while the value is bad, so its appearance is the proof the value reached C#, was
        // judged there, and came back as different markup; aria-describedby resolves to the visible text.
        var error = Page.Locator("[data-testid='ui-field'] [data-ui-error]").First;
        await Expect(error).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(email).ToHaveAttributeAsync("data-invalid", "");
        await Expect(email).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(email).ToHaveAccessibleDescriptionAsync("That does not look like an email address.");
        await email.FillAsync("ada@example.com");
        await email.BlurAsync();
        await Expect(error).ToBeHiddenAsync();
    });

    [Fact]
    public Task A_bound_control_writes_straight_to_the_model() => RunAsync(async () =>
    {
        await OpenUiPageAsync("field", "Field");
        var scope = Page.Locator("[data-testid='ui-bound']");
        var state = Page.Locator("[data-testid='ui-bound-state']");

        // No OnChange anywhere in this example: every control writes back through Bind, as it is typed (Live).
        await scope.Locator("input[type='email']").FillAsync("ada@example.com");
        await Expect(state).ToContainTextAsync("ada@example.com");

        // T comes off the model, so a bound int is a number field with nothing said at the call site.
        var seats = scope.Locator("input[type='number']");
        await Expect(seats).ToHaveCountAsync(1);
        await seats.FillAsync("4");
        await Expect(state).ToContainTextAsync("4 seats");
        await scope.Locator("[data-ui-checkbox]").ClickAsync();
        await Expect(scope.Locator("[data-ui-checkbox] input")).ToBeCheckedAsync();
        await Expect(state).ToContainTextAsync("agreed yes");
    });

    [Fact]
    public Task The_clear_button_appears_with_the_first_keystroke_and_empties_the_field() => RunAsync(async () =>
    {
        await OpenUiPageAsync("input", "Input");
        var scope = Page.Locator("[data-testid='ui-input-clearable']");
        var search = scope.Locator("input[placeholder='Search orders']");
        var clear = scope.Locator("[data-ui-clear-button]");
        await Expect(clear).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        await clear.ClickAsync();

        // Emptied by the runtime in the click, as Flux's is, with the focus left in the field; hidden by CSS alone
        // while empty, and back before any round trip.
        await Expect(search).ToHaveValueAsync("");
        await Expect(search).ToBeFocusedAsync();
        await Expect(clear).ToBeHiddenAsync();
        await search.PressSequentiallyAsync("a");
        await Expect(clear).ToBeVisibleAsync();
    });

    [Fact]
    public Task The_copy_button_copies_in_the_click_and_shows_its_tick_for_a_while() => RunAsync(async () =>
    {
        await OpenUiPageAsync("input", "Input");
        await Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        var copy = Page.Locator("[data-testid='ui-input-clearable'] button[aria-label='Copy to clipboard']");

        await copy.ClickAsync();

        await Expect(copy).ToHaveAttributeAsync("data-copied", "");
        Assert.Equal("FLUX-1234-5678-ABCD-EFGH", await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()"));
        await Expect(copy.Locator("svg").First).ToBeVisibleAsync();
        await Expect(copy.Locator("svg").Nth(1)).ToBeHiddenAsync();
    });

    [Fact]
    public Task A_masked_input_is_held_to_its_pattern_key_by_key() => RunAsync(async () =>
    {
        await OpenUiPageAsync("input", "Input");
        var phone = Page.Locator("[data-testid='ui-input-mask']").GetByLabel("Phone");
        await Expect(phone).ToHaveValueAsync("(716) 123-4567");

        // Drawn shaped by C#; what is typed is shaped by the runtime, a literal only once a character follows it.
        await phone.FillAsync("");
        await phone.PressSequentiallyAsync("5551");

        await Expect(phone).ToHaveValueAsync("(555) 1");
    });

    [Fact]
    public Task The_reveal_button_shows_the_password_and_hides_it_again() => RunAsync(async () =>
    {
        await OpenUiPageAsync("input", "Input");
        var toggle = Page.Locator("[data-testid='ui-input-clearable']").GetByLabel("Toggle password visibility");
        var field = toggle.Locator("xpath=ancestor::*[@data-ui-input][1]").Locator("input");
        await Expect(field).ToHaveAttributeAsync("type", "password", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });

        await toggle.ClickAsync();
        await Expect(field).ToHaveAttributeAsync("type", "text");
        await toggle.ClickAsync();

        await Expect(field).ToHaveAttributeAsync("type", "password");
    });

    [Fact]
    public Task A_textarea_reports_what_was_typed_and_an_auto_one_grows() => RunAsync(async () =>
    {
        await OpenUiPageAsync("textarea", "Textarea");
        var notes = Page.Locator("[data-testid='ui-textarea']").GetByLabel("Order notes");
        var auto = Page.Locator("[data-testid='ui-textarea-auto'] textarea");

        await notes.FillAsync("No onion.");
        await notes.BlurAsync();
        var before = (await auto.BoundingBoxAsync())!.Height;
        await auto.FillAsync("one\ntwo\nthree\nfour\nfive\nsix");

        await Expect(Page.Locator("[data-testid='ui-textarea-state']")).ToContainTextAsync("No onion.");
        Assert.True((await auto.BoundingBoxAsync())!.Height > before, "the auto-sizing textarea did not grow.");
    });

    [Fact]
    public Task A_check_all_ticks_the_group_and_clears_it_again() => RunAsync(async () =>
    {
        await OpenUiPageAsync("checkbox", "Checkbox");
        var all = Page.Locator("[data-testid='ui-checkbox-all'] label:has(> #cb-people-all)");
        var state = Page.Locator("[data-testid='ui-checkbox-all-state']");

        // One of three is ticked: the check-all is neither on nor off, and says so.
        await Expect(state).ToHaveTextAsync("Chosen: mara.");
        await Expect(all).ToHaveAttributeAsync("data-indeterminate", "");

        // Pressing it while some are ticked ticks them all; pressing it again clears them.
        await all.ClickAsync();
        await Expect(state).ToHaveTextAsync("Chosen: mara, theo, rosa.");
        await all.ClickAsync();
        await Expect(state).ToHaveTextAsync("Nobody chosen.");
    });

    [Fact]
    public Task A_checkbox_card_is_the_checkbox_and_the_space_bar_ticks_a_box() => RunAsync(async () =>
    {
        await OpenUiPageAsync("checkbox", "Checkbox");

        // A card is the checkbox: pressing anywhere on it ticks it.
        await Page.Locator("[data-testid='ui-checkbox-cards'] [data-ui-checkbox-cards]:has(> #cb-cards-updates)").ClickAsync();
        await Page.Locator("#cb-terms").FocusAsync();
        await Page.Keyboard.PressAsync("Space");

        // The space bar is the browser's: the input is real, however it is drawn.
        await Expect(Page.Locator("[data-testid='ui-checkbox-cards-state']")).ToHaveTextAsync("Subscribed to newsletter, updates.");
        await Expect(Page.Locator("[data-testid='ui-checkbox-state']")).ToHaveTextAsync("Terms agreed.");
    });

    [Fact]
    public Task A_radio_group_follows_the_arrow_keys_in_every_variant() => RunAsync(async () =>
    {
        await OpenUiPageAsync("radio", "Radio");
        var state = Page.Locator("[data-testid='ui-radio-state']");

        // Clicking a radio's label chooses it and focuses the input, with no script of the kit's.
        await Page.Locator("label[for='rd-payment-paypal']").ClickAsync();
        await Expect(state).ToHaveTextAsync("Paying by paypal.");
        await Expect(Page.Locator("#rd-payment-paypal")).ToBeFocusedAsync();

        // The arrows move AND choose, and wrap at the end of the group.
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(state).ToHaveTextAsync("Paying by ach.");
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(state).ToHaveTextAsync("Paying by cc.");

        // A segment and a card are the same radio: the arrows and a press work there too.
        await Page.Locator("#rd-segmented-administrator").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(Page.Locator("[data-testid='ui-radio-segmented-state']")).ToHaveTextAsync("Role: editor.");
        await Page.Locator("[data-testid='ui-radio-cards'] [data-ui-radio-cards]:has(> #rd-cards-fast)").ClickAsync();
        await Expect(Page.Locator("[data-testid='ui-radio-cards-state']")).ToHaveTextAsync("Shipping fast.");
    });

    [Fact]
    public Task A_switch_flips_on_a_click_on_its_label_and_on_Space_and_Enter() => RunAsync(async () =>
    {
        await OpenUiPageAsync("switch", "Switch");
        var state = Page.Locator("[data-testid='ui-switch-state']");
        var notify = Page.Locator("#sw-notify");
        await Expect(notify).ToHaveRoleAsync(AriaRole.Switch);

        await Page.Locator("label[for='sw-notify']").ClickAsync();
        await Expect(state).ToHaveTextAsync("Notifications on.");
        await Expect(notify).ToBeCheckedAsync();
        await Page.Keyboard.PressAsync("Space");
        await Expect(state).ToHaveTextAsync("Notifications off.");

        // Flux's switch flips on Enter too; a native checkbox does not, so the runtime does it for role="switch".
        await Page.Keyboard.PressAsync("Enter");
        await Expect(state).ToHaveTextAsync("Notifications on.");
        await Page.Keyboard.PressAsync("Enter");
        await Expect(state).ToHaveTextAsync("Notifications off.");
    });

    [Fact]
    public Task The_slider_follows_the_keyboard_and_reports_its_value() => RunAsync(async () =>
    {
        await OpenUiPageAsync("slider", "Slider");
        var value = Page.Locator("[data-testid='ui-slider-amount']");
        var input = Page.Locator("[data-testid='ui-slider-value'] input[type='range']");
        await Expect(value).ToHaveTextAsync("50");

        // A native range input, so the keys are the browser's: an arrow is one step, Page Up a tenth of the track,
        // Home and End the two ends. Each reaches C# and comes back as the drawn value.
        await input.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(value).ToHaveTextAsync("51");
        await Page.Keyboard.PressAsync("PageUp");
        await Expect(value).ToHaveTextAsync("61");
        await Page.Keyboard.PressAsync("Home");
        await Expect(value).ToHaveTextAsync("0");
        await Page.Keyboard.PressAsync("End");

        await Expect(value).ToHaveTextAsync("100");
    });

    [Fact]
    public Task Shift_with_an_arrow_and_the_page_keys_move_the_slider_by_its_big_step() => RunAsync(async () =>
    {
        await OpenUiPageAsync("slider", "Slider");
        var value = Page.Locator("[data-testid='ui-slider-big-value']");
        await Expect(value).ToHaveTextAsync("50");
        await Page.Locator("[data-testid='ui-slider-big'] input[type='range']").FocusAsync();

        // Flux's numbers, on its own "Big steps" example: step 1, big-step 10.
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(value).ToHaveTextAsync("51");
        await Page.Keyboard.PressAsync("Shift+ArrowRight");
        await Expect(value).ToHaveTextAsync("61");
        await Page.Keyboard.PressAsync("Shift+ArrowUp");
        await Expect(value).ToHaveTextAsync("71");
        await Page.Keyboard.PressAsync("Shift+ArrowLeft");
        await Expect(value).ToHaveTextAsync("61");
        await Page.Keyboard.PressAsync("PageDown");
        await Expect(value).ToHaveTextAsync("51");
        await Page.Keyboard.PressAsync("PageUp");
        await Expect(value).ToHaveTextAsync("61");
        await Page.Keyboard.PressAsync("End");
        await Page.Keyboard.PressAsync("Shift+ArrowRight");

        await Expect(value).ToHaveTextAsync("100");
    });

    [Fact]
    public Task Pressing_the_track_moves_the_thumb_under_the_pointer_and_dragging_carries_it() => RunAsync(async () =>
    {
        await OpenUiPageAsync("slider", "Slider");
        var value = Page.Locator("[data-testid='ui-slider-amount']");
        var slider = Page.Locator("[data-testid='ui-slider-value'] [data-ui-slider]");
        var thumb = slider.Locator("[data-ui-slider-thumb]");
        await slider.ScrollIntoViewIfNeededAsync();
        var box = (await slider.BoundingBoxAsync())!;
        var middle = box.Y + (box.Height / 2);

        // A quarter of the way along: the value under the pointer, counted between the thumb's two resting ends.
        await Page.Mouse.ClickAsync(box.X + (box.Width * 0.25f), middle);
        var expected = (int)Math.Round(((box.Width * 0.25) - 8) / (box.Width - 16) * 100, MidpointRounding.AwayFromZero);
        await Expect(value).ToHaveTextAsync(expected.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var drawn = (await thumb.BoundingBoxAsync())!;
        Assert.InRange(drawn.X + (drawn.Width / 2) - (box.X + (box.Width * 0.25f)), -2, 2);
        await Page.Mouse.MoveAsync(box.X + (box.Width * 0.25f), middle);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(box.X + box.Width - 1, middle, new MouseMoveOptions { Steps = 6 });
        await Page.Mouse.UpAsync();

        await Expect(value).ToHaveTextAsync("100");
    });

    [Fact]
    public Task Pressing_a_tick_moves_the_thumb_to_it() => RunAsync(async () =>
    {
        await OpenUiPageAsync("slider", "Slider");
        var slider = Page.Locator("[data-testid='ui-slider-ticks'] [data-ui-slider]");
        var input = slider.Locator("input[type='range']");
        await Expect(input).ToHaveValueAsync("3");

        await slider.Locator("[data-ui-slider-tick]", new LocatorLocatorOptions { HasText = "High" }).ClickAsync();

        await Expect(input).ToHaveValueAsync("5");
        await Expect(slider.Locator("[data-ui-slider-tick][data-current]")).ToHaveTextAsync("High");
    });

    [Fact]
    public Task A_range_slider_moves_the_nearer_thumb_and_the_thumbs_do_not_cross() => RunAsync(async () =>
    {
        await OpenUiPageAsync("slider", "Slider");
        var price = Page.Locator("[data-testid='ui-slider-price']");
        var slider = Page.Locator("[data-testid='ui-slider-prices'] [data-ui-field] [data-ui-slider]");
        var thumbs = slider.Locator("input[type='range']");
        await slider.ScrollIntoViewIfNeededAsync();
        var box = (await slider.BoundingBoxAsync())!;
        var middle = box.Y + (box.Height / 2);
        await Expect(price).ToHaveTextAsync("$200 – $800");
        await Expect(thumbs).ToHaveCountAsync(2);

        // A press near the left end is the first thumb's; one near the right end, the second's.
        await Page.Mouse.ClickAsync(box.X + 1, middle);
        await Expect(price).ToHaveTextAsync("$0 – $800");
        await Page.Mouse.ClickAsync(box.X + box.Width - 1, middle);
        await Expect(price).ToHaveTextAsync("$0 – $990");
        await thumbs.First.FocusAsync();
        await Page.Keyboard.PressAsync("End");

        // End on the first thumb stops ten steps short of the second: MinStepsBetween(10) at a step of 10.
        await Expect(price).ToHaveTextAsync("$890 – $990");
    });

    [Fact]
    public Task The_one_time_code_is_a_cell_per_character_that_spells_one_code() => RunAsync(async () =>
    {
        await OpenUiPageAsync("otp-input", "OTP input");
        var cells = OtpCells("ui-otp");
        var state = Page.Locator("[data-testid='ui-otp-state']");

        // Flux's shape: a real text input per character, each named by its place, one of them a tab stop.
        await Expect(cells).ToHaveCountAsync(6);
        await Expect(cells.Nth(0)).ToHaveAccessibleNameAsync("Character 1 of 6");
        await Expect(cells.Nth(0)).ToHaveAttributeAsync("autocomplete", "one-time-code");
        await Expect(cells.Nth(0)).ToHaveAttributeAsync("tabindex", "0");
        await Expect(cells.Nth(1)).ToHaveAttributeAsync("tabindex", "-1");

        // A character moves on to the next cell, and the code reaches C#.
        await cells.Nth(0).ClickAsync();
        await Page.Keyboard.PressAsync("1");
        await Expect(cells.Nth(1)).ToBeFocusedAsync();
        await Expect(state).ToContainTextAsync("1 of 6 entered");
        await Expect(cells.Nth(1)).ToHaveAttributeAsync("tabindex", "0");

        // A letter is refused by a numeric code; the rest typed with no pause between keys lands a cell each.
        await Page.Keyboard.PressAsync("a");
        await Page.Keyboard.TypeAsync("23456");
        await Expect(state).ToContainTextAsync("Code complete: 123456.");
        Assert.Equal("1 2 3 4 5 6", await OtpTextAsync("ui-otp"));
    });

    [Fact]
    public Task Typing_over_a_cell_replaces_it_and_backspace_walks_back_through_the_code() => RunAsync(async () =>
    {
        await OpenUiPageAsync("otp-input", "OTP input");
        var cells = OtpCells("ui-otp");
        var state = Page.Locator("[data-testid='ui-otp-state']");
        await cells.Nth(0).ClickAsync();
        await Page.Keyboard.TypeAsync("123");

        // ArrowLeft selects the cell before, so the next key replaces its character and moves on.
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Page.Keyboard.PressAsync("9");
        Assert.Equal("1 9 3 _ _ _", await OtpTextAsync("ui-otp"));
        await Expect(cells.Nth(2)).ToBeFocusedAsync();
        await Expect(state).ToContainTextAsync("3 of 6 entered");

        // A press past the first empty cell lands on it; ArrowRight stops there too.
        await cells.Nth(5).ClickAsync();
        await Expect(cells.Nth(3)).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(cells.Nth(3)).ToBeFocusedAsync();

        // Backspace in an empty cell only steps back; in a filled one it deletes and steps back.
        await Page.Keyboard.PressAsync("Backspace");
        Assert.Equal("1 9 3 _ _ _", await OtpTextAsync("ui-otp"));
        await Page.Keyboard.PressAsync("Backspace");
        Assert.Equal("1 9 _ _ _ _", await OtpTextAsync("ui-otp"));
        await Expect(state).ToContainTextAsync("2 of 6 entered");

        // Delete closes the code up from where it is.
        await cells.Nth(0).ClickAsync();
        await Page.Keyboard.PressAsync("Delete");
        Assert.Equal("9 _ _ _ _ _", await OtpTextAsync("ui-otp"));
        await Expect(state).ToContainTextAsync("1 of 6 entered");
    });

    [Fact]
    public Task A_pasted_code_fills_the_cells_from_the_first_and_reaches_CSharp() => RunAsync(async () =>
    {
        await OpenUiPageAsync("otp-input", "OTP input");
        await OtpCells("ui-otp").Nth(0).ClickAsync();
        await Page.Keyboard.TypeAsync("12");

        await Page.EvaluateAsync(
            """
            () => {
                const data = new DataTransfer();
                data.setData('text/plain', '98-76 54zz');
                document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
            }
            """);

        Assert.Equal("9 8 7 6 5 4", await OtpTextAsync("ui-otp"));
        await Expect(Page.Locator("[data-testid='ui-otp-state']")).ToContainTextAsync("Code complete: 987654.");
    });

    [Fact]
    public Task Letters_reach_CSharp_upper_cased_and_a_code_set_in_CSharp_fills_the_cells() => RunAsync(async () =>
    {
        await OpenUiPageAsync("otp-input", "OTP input");

        // The licence key arrives from C# ("L49R4"): the runtime shares it out over the cells.
        var license = OtpCells("ui-otp-alphanumeric");
        await Expect(license.Nth(0)).ToHaveValueAsync("L");
        await Expect(license.Nth(4)).ToHaveValueAsync("4");
        await Expect(license.Nth(5)).ToHaveValueAsync(string.Empty);
        await license.Nth(9).ClickAsync();
        await Page.Keyboard.TypeAsync("ab");
        await Expect(Page.Locator("[data-testid='ui-otp-license']")).ToHaveTextAsync("Key: L49R4AB");

        // "Resend code" empties the string in C#, and the cells follow it.
        var form = OtpCells("ui-otp-form");
        await form.Nth(0).ClickAsync();
        await Page.Keyboard.TypeAsync("4321");
        await Expect(form.Nth(3)).ToHaveValueAsync("1");
        await Page.Locator("[data-testid='ui-otp-resend']").ClickAsync();
        await Expect(form.Nth(0)).ToHaveValueAsync(string.Empty);
        await Expect(form.Nth(3)).ToHaveValueAsync(string.Empty);
    });

    [Fact]
    public Task A_complete_one_time_code_runs_its_completion_handler() => RunAsync(async () =>
    {
        await OpenUiPageAsync("otp-input", "OTP input");
        var verified = Page.Locator("[data-testid='ui-otp-verified']");
        await Expect(verified).ToContainTextAsync("Fill every cell");

        await OtpCells("ui-otp-auto").Nth(0).ClickAsync();
        await Page.Keyboard.TypeAsync("654321");

        await Expect(verified).ToContainTextAsync("Verifying 654321");
    });

    [Fact]
    public Task One_time_code_cells_join_into_groups_around_a_separator() => RunAsync(async () =>
    {
        await OpenUiPageAsync("otp-input", "OTP input");

        var grouped = Page.Locator("[data-testid='ui-otp-groups'] [data-ui-otp]");

        await Expect(grouped.Locator("[data-ui-input-group]")).ToHaveCountAsync(2);
        await Expect(grouped.Locator("input[data-ui-otp-input]")).ToHaveCountAsync(6);
        await Expect(grouped.Locator("[data-ui-text]")).ToHaveTextAsync("—");

        // Joined: a middle cell of a group has no left border and square corners.
        var middle = grouped.Locator("input[data-ui-otp-input]").Nth(1);
        Assert.Equal("0px", await middle.EvaluateAsync<string>("e => getComputedStyle(e).borderLeftWidth"));
        Assert.Equal("0px", await middle.EvaluateAsync<string>("e => getComputedStyle(e).borderTopLeftRadius"));
    });

    private ILocator OtpCells(string testid) =>
        Page.Locator($"[data-testid='{testid}'] [data-ui-otp]").Locator("input[data-ui-otp-input]");

    // The cells as text, an underscore for an empty one: "1 9 3 _ _ _".
    private Task<string> OtpTextAsync(string testid) =>
        Page.Locator($"[data-testid='{testid}'] [data-ui-otp]").EvaluateAsync<string>(
            "group => [...group.querySelectorAll('input[data-ui-otp-input]')].map(cell => cell.value || '_').join(' ')");
}
