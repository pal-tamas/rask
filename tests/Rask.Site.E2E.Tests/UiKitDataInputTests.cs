using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's Data input components, in a browser.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitDataInputTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Every_data_input_component_renders_with_a_real_size() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-text-controls", "ui-input-group", "ui-textarea", "ui-select", "ui-listbox", "ui-select-search", "ui-combobox", "ui-autocomplete", "ui-pillbox", "ui-pillbox-combobox", "ui-checkbox", "ui-radio", "ui-switch", "ui-slider", "ui-slider-ticks", "ui-slider-range", "ui-rating", "ui-otp", "ui-otp-layout", "ui-filter",
                     "ui-calendar", "ui-dates", "ui-dropzone", "ui-bound", "ui-mask",
                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height.");
        }
    });

    [Fact]
    public Task Typing_into_a_field_reaches_CSharp_and_comes_back() => RunAsync(async () =>
    {
        await OpenAsync();

        var email = Page.Locator("[data-testid='ui-text-controls'] input[type='email']");

        // Described by its hint while valid; the badge beside the label is not part of the field's name.
        await Expect(email).ToHaveAccessibleDescriptionAsync("For example, you@example.com.");
        await Expect(email).ToHaveAccessibleNameAsync("Email");

        await email.FillAsync("not-an-address");
        await email.BlurAsync();

        // The error only shows while the value is bad, so its appearance is the proof the value reached C#,
        // was judged there, and came back as different markup.
        var error = Page.Locator("[data-testid='ui-text-controls'] [data-ui-error]").First;
        await Expect(error).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(email).ToHaveAttributeAsync("data-invalid", "");

        // aria-describedby resolves to the VISIBLE text, error first — what a screen reader reads with the field.
        await Expect(email).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(email).ToHaveAccessibleDescriptionAsync(
            "That does not look like an email address. For example, you@example.com.");

        await email.FillAsync("ada@example.com");
        await email.BlurAsync();
        await Expect(error).ToBeHiddenAsync();
        await Expect(email).ToHaveAccessibleDescriptionAsync("For example, you@example.com.");
    });

    [Fact]
    public Task The_clear_button_appears_with_the_first_keystroke_and_empties_the_field() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-text-controls']");
        var search = scope.Locator("input[placeholder='Search orders']").Nth(2);
        var clear = scope.Locator("[data-ui-clear-button]");
        await Expect(clear).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        await clear.ClickAsync();

        // Emptied by the runtime in the click, as Flux's is, with the focus left in the field; hidden by CSS
        // alone while empty, and back before any round trip.
        await Expect(search).ToHaveValueAsync("");
        await Expect(search).ToBeFocusedAsync();
        await Expect(clear).ToBeHiddenAsync();
        await search.PressSequentiallyAsync("a");
        await Expect(clear).ToBeVisibleAsync();
    });

    [Fact]
    public Task The_copy_button_copies_in_the_click_and_shows_its_tick_for_a_while() => RunAsync(async () =>
    {
        await OpenAsync();
        await Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        var copy = Page.Locator("[data-testid='ui-text-controls'] button[aria-label='Copy to clipboard']");
        await copy.ClickAsync();

        await Expect(copy).ToHaveAttributeAsync("data-copied", "");
        Assert.Equal("FLUX-1234-5678-ABCD-EFGH", await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()"));
        await Expect(copy.Locator("svg").First).ToBeVisibleAsync();
        await Expect(copy.Locator("svg").Nth(1)).ToBeHiddenAsync();
    });

    [Fact]
    public Task A_masked_input_is_held_to_its_pattern_key_by_key() => RunAsync(async () =>
    {
        await OpenAsync();

        // Drawn shaped by C#; what is typed is shaped by the runtime, a literal only once a character follows it.
        var phone = Page.Locator("[data-testid='ui-text-controls']").GetByLabel("Phone, masked");
        await Expect(phone).ToHaveValueAsync("(716) 123-4567");
        await phone.FillAsync("");
        await phone.PressSequentiallyAsync("5551");

        await Expect(phone).ToHaveValueAsync("(555) 1");
    });

    [Fact]
    public Task The_reveal_button_shows_the_password_and_hides_it_again() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-text-controls']");
        var toggle = scope.GetByLabel("Toggle password visibility");
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
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-textarea']");
        var notes = scope.GetByLabel("Order notes");
        await notes.FillAsync("No onion.");
        await notes.BlurAsync();
        await Expect(Page.Locator("[data-testid='ui-textarea-state']")).ToContainTextAsync("No onion.");

        var auto = scope.GetByPlaceholder("This textarea will adjust to fit the content...");
        var before = (await auto.BoundingBoxAsync())!.Height;
        await auto.FillAsync("one\ntwo\nthree\nfour");

        Assert.True((await auto.BoundingBoxAsync())!.Height > before, "the auto-sizing textarea did not grow.");
    });

    [Fact]
    public Task The_one_time_code_is_a_cell_per_character_that_spells_one_code() => RunAsync(async () =>
    {
        await OpenAsync();

        var cells = OtpCells(0);
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
        Assert.Equal("1 2 3 4 5 6", await OtpTextAsync(0));
    });

    [Fact]
    public Task Typing_over_a_cell_replaces_it_and_backspace_walks_back_through_the_code() => RunAsync(async () =>
    {
        await OpenAsync();

        var cells = OtpCells(0);
        var state = Page.Locator("[data-testid='ui-otp-state']");
        await cells.Nth(0).ClickAsync();
        await Page.Keyboard.TypeAsync("123");

        // ArrowLeft selects the cell before, so the next key replaces its character and moves on.
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Page.Keyboard.PressAsync("9");
        Assert.Equal("1 9 3 _ _ _", await OtpTextAsync(0));
        await Expect(cells.Nth(2)).ToBeFocusedAsync();
        await Expect(state).ToContainTextAsync("3 of 6 entered");

        // A press past the first empty cell lands on it; ArrowRight stops there too.
        await cells.Nth(5).ClickAsync();
        await Expect(cells.Nth(3)).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(cells.Nth(3)).ToBeFocusedAsync();

        // Backspace in an empty cell only steps back; in a filled one it deletes and steps back.
        await Page.Keyboard.PressAsync("Backspace");
        Assert.Equal("1 9 3 _ _ _", await OtpTextAsync(0));
        await Page.Keyboard.PressAsync("Backspace");
        Assert.Equal("1 9 _ _ _ _", await OtpTextAsync(0));
        await Expect(state).ToContainTextAsync("2 of 6 entered");

        // Delete closes the code up from where it is.
        await cells.Nth(0).ClickAsync();
        await Page.Keyboard.PressAsync("Delete");
        Assert.Equal("9 _ _ _ _ _", await OtpTextAsync(0));
        await Expect(state).ToContainTextAsync("1 of 6 entered");
    });

    [Fact]
    public Task A_pasted_code_fills_the_cells_from_the_first_and_reaches_CSharp() => RunAsync(async () =>
    {
        await OpenAsync();

        var cells = OtpCells(0);
        var state = Page.Locator("[data-testid='ui-otp-state']");
        await cells.Nth(0).ClickAsync();
        await Page.Keyboard.TypeAsync("12");

        await Page.EvaluateAsync(
            """
            () => {
                const data = new DataTransfer();
                data.setData('text/plain', '98-76 54zz');
                document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
            }
            """);

        Assert.Equal("9 8 7 6 5 4", await OtpTextAsync(0));
        await Expect(state).ToContainTextAsync("Code complete: 987654.");
    });

    [Fact]
    public Task Letters_reach_CSharp_upper_cased_and_a_code_set_in_CSharp_fills_the_cells() => RunAsync(async () =>
    {
        await OpenAsync();

        // The licence key arrives from C# ("L49R4"): the runtime shares it out over the cells.
        var license = OtpCells(3);
        await Expect(license.Nth(0)).ToHaveValueAsync("L");
        await Expect(license.Nth(4)).ToHaveValueAsync("4");
        await Expect(license.Nth(5)).ToHaveValueAsync(string.Empty);

        await license.Nth(9).ClickAsync();
        await Page.Keyboard.TypeAsync("ab");
        await Expect(Page.Locator("[data-testid='ui-otp-license']")).ToHaveTextAsync("Key: L49R4AB");

        // "Resend code" empties the string in C#, and the cells follow it.
        var form = OtpCells(1);
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
        await OpenAsync();

        var verified = Page.Locator("[data-testid='ui-otp-verified']");

        await Expect(verified).ToContainTextAsync("Fill every cell");
        await OtpCells(2).Nth(0).ClickAsync();
        await Page.Keyboard.TypeAsync("654321");

        await Expect(verified).ToContainTextAsync("Verifying 654321");
    });

    [Fact]
    public Task One_time_code_cells_join_into_groups_around_a_separator() => RunAsync(async () =>
    {
        await OpenAsync();

        var grouped = Page.Locator("[data-testid='ui-otp-layout'] [data-ui-otp]").Nth(2);

        await Expect(grouped.Locator("[data-ui-input-group]")).ToHaveCountAsync(2);
        await Expect(grouped.Locator("input[data-ui-otp-input]")).ToHaveCountAsync(6);
        await Expect(grouped.Locator("[data-ui-text]")).ToHaveTextAsync("—");

        // Joined: a middle cell of a group has no left border and square corners.
        var middle = grouped.Locator("input[data-ui-otp-input]").Nth(1);
        Assert.Equal("0px", await middle.EvaluateAsync<string>("e => getComputedStyle(e).borderLeftWidth"));
        Assert.Equal("0px", await middle.EvaluateAsync<string>("e => getComputedStyle(e).borderTopLeftRadius"));
    });

    [Fact]
    public Task The_filter_narrows_and_resets() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-filter']");
        var state = Page.Locator("[data-testid='ui-filter-state']");

        await Expect(state).ToContainTextAsync("Showing everything");

        await scope.Locator("input[value='feature']").CheckAsync();
        await Expect(state).ToContainTextAsync("Filtered to feature");

        await scope.Locator(".filter-reset").CheckAsync();
        await Expect(state).ToContainTextAsync("Showing everything");
    });

    [Fact]
    public Task The_calendar_changes_month_and_picks_a_day() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-calendar']");
        var state = Page.Locator("[data-testid='ui-calendar-state']");

        await Expect(state).ToContainTextAsync("No date chosen");

        // Paging months is an ordinary re-render — there is no web component here to ask.
        var heading = scope.Locator(".text-sm.font-semibold").First;
        var before = await heading.TextContentAsync();
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Next month" })
            .ClickAsync();
        await Expect(heading).Not.ToHaveTextAsync(before ?? "");

        await scope.Locator("table button:not([disabled])").First.ClickAsync();
        await Expect(state).ToContainTextAsync("Chosen:");
    });

    [Fact]
    public Task A_bound_control_writes_straight_to_the_model() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-bound']");
        var state = Page.Locator("[data-testid='ui-bound-state']");

        // No OnChange anywhere in this section: every one of these writes back through Bind. The
        // markup assertions in Rask.Ui.Tests can only see what a control DRAWS — this is the half that
        // proves the write-back reaches the model over a live session.
        await scope.Locator("input[type='email']").FillAsync("ada@example.com");
        await scope.Locator("input[type='email']").BlurAsync();
        await Expect(state).ToContainTextAsync("ada@example.com");

        // T comes off the model, so a bound int is a number field with nothing said at the call site.
        var seats = scope.Locator("input[type='number']");
        await Expect(seats).ToHaveCountAsync(1);
        await seats.FillAsync("4");
        await seats.BlurAsync();
        await Expect(state).ToContainTextAsync("4 seats");

        // The input is out of sight inside its label; the box is what a reader presses.
        await scope.Locator("[data-ui-checkbox]").ClickAsync();
        await Expect(scope.Locator("[data-ui-checkbox] input")).ToBeCheckedAsync();
        await Expect(state).ToContainTextAsync("agreed yes");

        // A rating is radios sharing a name, and a bound radio's state is `checked` rather than a
        // value attribute — which is exactly what used to be wrong.
        await scope.Locator("input.mask-star-2").Nth(2).CheckAsync();
        await Expect(state).ToContainTextAsync("3 stars");
    });

    [Fact]
    public Task A_checkbox_group_and_its_check_all_write_the_bound_collection() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-checkbox']");
        var state = Page.Locator("[data-testid='ui-checkbox-state']");
        var all = scope.Locator("#cb-people-all");

        // One of three is ticked: the check-all is neither on nor off, and says so.
        await Expect(state).ToContainTextAsync("people caleb");
        await Expect(scope.Locator("label:has(> #cb-people-all)")).ToHaveAttributeAsync("data-indeterminate", "");

        // Pressing it while some are ticked ticks them all; pressing it again clears them.
        await scope.Locator("label:has(> #cb-people-all)").ClickAsync();
        await Expect(state).ToContainTextAsync("people caleb, hugo, keith");
        await Expect(all).ToBeCheckedAsync();
        await scope.Locator("label:has(> #cb-people-all)").ClickAsync();
        await Expect(state).ToContainTextAsync("people nothing");

        // A card is the checkbox: pressing anywhere on it ticks it, and every example over the same member follows.
        await scope.Locator("[data-ui-checkbox-cards]:has(> #cb-cards-updates)").ClickAsync();
        await Expect(state).ToContainTextAsync("subscribed to newsletter, updates");
        await Expect(scope.Locator("#cb-described-updates")).ToBeCheckedAsync();

        // The space bar is the browser's: the input is real, however it is drawn.
        await scope.Locator("#cb-terms").FocusAsync();
        await Page.Keyboard.PressAsync("Space");
        await Expect(state).ToContainTextAsync("terms agreed");
    });

    [Fact]
    public Task A_radio_group_follows_the_arrow_keys_in_every_variant() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-radio']");
        var state = Page.Locator("[data-testid='ui-radio-state']");

        // Clicking a radio's label chooses it and focuses the input, with no script of the kit's.
        await scope.Locator("label[for='rd-payment-paypal']").ClickAsync();
        await Expect(state).ToContainTextAsync("paying by paypal");
        await Expect(scope.Locator("#rd-payment-paypal")).ToBeFocusedAsync();

        // The arrows move AND choose, and wrap at the end of the group.
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(state).ToContainTextAsync("paying by ach");
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(state).ToContainTextAsync("paying by cc");

        // A segment is the same radio: the arrow keys work there too, and the list over the same member follows.
        await scope.Locator("#rd-segmented-administrator").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(state).ToContainTextAsync("editor");
        await Expect(scope.Locator("#rd-described-editor")).ToBeCheckedAsync();

        await scope.Locator("[data-ui-radio-cards]:has(> #rd-cards-fast)").ClickAsync();
        await Expect(state).ToContainTextAsync("shipping fast");
    });

    [Fact]
    public Task A_switch_flips_on_a_click_on_its_label_and_on_Space_and_Enter() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-switch']");
        var state = Page.Locator("[data-testid='ui-switch-state']");
        var notify = scope.Locator("#sw-notify");

        await Expect(notify).ToHaveRoleAsync(AriaRole.Switch);
        await scope.Locator("label[for='sw-notify']").ClickAsync();
        await Expect(state).ToContainTextAsync("notifications on");
        await Expect(notify).ToBeCheckedAsync();

        await Page.Keyboard.PressAsync("Space");
        await Expect(state).ToContainTextAsync("notifications off");

        // Flux's switch flips on Enter too; a native checkbox does not, so the runtime does it for role="switch".
        await Page.Keyboard.PressAsync("Enter");
        await Expect(state).ToContainTextAsync("notifications on");
        await Page.Keyboard.PressAsync("Enter");
        await Expect(state).ToContainTextAsync("notifications off");

        // Two switches over one member: flipping the left-aligned one flips its twin in the fieldset.
        await scope.Locator("[data-ui-switch]:has(> #sw-left-marketing)").ClickAsync();
        await Expect(state).ToContainTextAsync("marketing on");
        await Expect(scope.Locator("#sw-marketing")).ToBeCheckedAsync();
        await Expect(scope.Locator("#sw-left-security")).ToBeDisabledAsync();
    });

    [Fact]
    public Task The_slider_follows_the_keyboard_and_reports_its_value() => RunAsync(async () =>
    {
        await OpenAsync();

        var value = Page.Locator("[data-testid='ui-slider-value']");
        var slider = Page.Locator("[data-testid='ui-slider'] [data-ui-field]").First.Locator("[data-ui-slider]");
        var input = slider.Locator("input[type='range']");

        await Expect(value).ToHaveTextAsync("50");

        // The control is a native range input, so the keys are the browser's: an arrow is one step, Page Up a
        // tenth of the track, Home and End the two ends. Each reaches C# and comes back as the drawn value.
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
        await OpenAsync();

        var value = Page.Locator("[data-testid='ui-slider-big']");
        var input = Page.Locator("[data-testid='ui-slider'] [data-ui-field]:has([data-testid='ui-slider-big']) input[type='range']");
        await Expect(value).ToHaveTextAsync("500");
        await input.FocusAsync();

        // Flux's numbers, on its own "Big steps" example (step 1, big-step 100, 0 to 1000).
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(value).ToHaveTextAsync("501");
        await Page.Keyboard.PressAsync("Shift+ArrowRight");
        await Expect(value).ToHaveTextAsync("601");
        await Page.Keyboard.PressAsync("Shift+ArrowUp");
        await Expect(value).ToHaveTextAsync("701");
        await Page.Keyboard.PressAsync("Shift+ArrowLeft");
        await Expect(value).ToHaveTextAsync("601");
        await Page.Keyboard.PressAsync("PageDown");
        await Expect(value).ToHaveTextAsync("501");
        await Page.Keyboard.PressAsync("PageUp");
        await Expect(value).ToHaveTextAsync("601");
        await Page.Keyboard.PressAsync("End");
        await Page.Keyboard.PressAsync("Shift+ArrowRight");
        await Expect(value).ToHaveTextAsync("1000");
    });

    [Fact]
    public Task Pressing_the_track_moves_the_thumb_under_the_pointer_and_dragging_carries_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var value = Page.Locator("[data-testid='ui-slider-value']");
        var slider = Page.Locator("[data-testid='ui-slider'] [data-ui-field]").First.Locator("[data-ui-slider]");
        var thumb = slider.Locator("[data-ui-slider-thumb]");
        await slider.ScrollIntoViewIfNeededAsync();
        var box = (await slider.BoundingBoxAsync())!;
        var middle = box.Y + (box.Height / 2);

        // A quarter of the way along: the value under the pointer, counted between the thumb's two resting ends.
        await Page.Mouse.ClickAsync(box.X + (box.Width * 0.25f), middle);
        var expected = (int)Math.Round(((box.Width * 0.25) - 8) / (box.Width - 16) * 100, MidpointRounding.AwayFromZero);
        await Expect(value).ToHaveTextAsync(expected.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // The drawn thumb is where the value is: centred on the pointer, to the pixel the step allows.
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
        await OpenAsync();

        var slider = Page.Locator("[data-testid='ui-slider-ticks'] [data-ui-slider]").Last;
        var input = slider.Locator("input[type='range']");

        await Expect(input).ToHaveValueAsync("3");
        await slider.Locator("[data-ui-slider-tick]", new LocatorLocatorOptions { HasText = "High" }).ClickAsync();

        await Expect(input).ToHaveValueAsync("5");
        await Expect(slider.Locator("[data-ui-slider-tick][data-current]")).ToHaveTextAsync("High");
    });

    [Fact]
    public Task A_range_slider_moves_the_nearer_thumb_and_the_thumbs_do_not_cross() => RunAsync(async () =>
    {
        await OpenAsync();

        var price = Page.Locator("[data-testid='ui-slider-price']");
        var slider = Page.Locator("[data-testid='ui-slider-range'] [data-ui-field] [data-ui-slider]");
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

        // End on the first thumb stops ten steps short of the second: MinStepsBetween(10) at a step of 10.
        await thumbs.First.FocusAsync();
        await Page.Keyboard.PressAsync("End");
        await Expect(price).ToHaveTextAsync("$890 – $990");
    });

    [Fact]
    public Task The_native_select_reports_its_pick() => RunAsync(async () =>
    {
        await OpenAsync();

        await Page.Locator("#ui-select-native").SelectOptionAsync("Accounting");

        await Expect(Page.Locator("[data-testid='ui-select-state']")).ToContainTextAsync("Chosen: Accounting.");
    });

    [Fact]
    public Task The_listbox_opens_walks_and_picks_from_the_keyboard() => RunAsync(async () =>
    {
        await OpenAsync();
        var box = Page.Locator("#ui-select-listbox");
        var list = Page.Locator("[data-testid='ui-listbox'] [role='listbox']").First;

        // An arrow opens it, as on Flux; focus stays on the button and aria-activedescendant names the row.
        await box.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(list).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");
        await Page.Keyboard.PressAsync("ArrowDown");
        var active = await box.GetAttributeAsync("aria-activedescendant");
        await Page.Keyboard.PressAsync("Enter");

        Assert.False(string.IsNullOrEmpty(active), "the cursor did not move");
        await Expect(Page.Locator("[data-testid='ui-listbox-state']")).ToContainTextAsync("Chosen: Design services.");
        await Expect(list).ToBeHiddenAsync();
        await Expect(box).ToBeFocusedAsync();
    });

    [Fact]
    public Task Enter_leaves_a_closed_listbox_shut_and_an_open_one_locks_the_page_behind_it() => RunAsync(async () =>
    {
        await OpenAsync();
        var box = Page.Locator("#ui-select-listbox");

        // Flux's button is not a native one: Enter does not press it. An arrow opens it without scrolling the page.
        await box.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
        var before = (await box.BoundingBoxAsync())!.Y;
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");

        Assert.Equal(before, (await box.BoundingBoxAsync())!.Y);
        Assert.Equal("hidden", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overflowY"));
        Assert.Equal("none", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).pointerEvents"));
        await Page.Keyboard.PressAsync("Escape");
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Equal("visible", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overflowY"));
    });

    [Fact]
    public Task Escape_closes_the_listbox_and_CSharp_hears_it() => RunAsync(async () =>
    {
        await OpenAsync();
        var box = Page.Locator("#ui-select-listbox");

        await box.ClickAsync();
        await Expect(box).ToHaveAttributeAsync("aria-expanded", "true");
        await Page.Keyboard.PressAsync("Escape");

        await Expect(box).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(box).ToBeFocusedAsync();
    });

    [Fact]
    public Task The_open_list_hangs_under_its_button_as_wide_as_it_is() => RunAsync(async () =>
    {
        await OpenAsync();
        var box = Page.Locator("#ui-select-listbox");
        var list = Page.Locator("[data-testid='ui-listbox'] [data-ui-options]").First;

        await box.ClickAsync();
        await Expect(list).ToBeVisibleAsync();
        var button = await box.BoundingBoxAsync();
        var popup = await list.BoundingBoxAsync();

        // Flux's gap is five pixels; the list lines up with the button's start and takes its width.
        Assert.Equal(button!.X, popup!.X, 1);
        Assert.Equal(button.Width, popup.Width, 1);
        Assert.Equal(button.Y + button.Height + 5, popup.Y, 1);
    });

    [Fact]
    public Task The_searchable_listbox_takes_the_keys_and_filters_as_it_is_typed_into() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-select-search']");

        await Page.Locator("#ui-select-searchable").ClickAsync();
        await Expect(scope.Locator("[data-ui-select-search] input").First).ToBeFocusedAsync();
        await Page.Keyboard.TypeAsync("leg");
        await Expect(scope.Locator("[data-ui-option]:visible")).ToHaveCountAsync(1);
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page.Locator("[data-testid='ui-select-search-state']")).ToContainTextAsync("Chosen: Legal services.");
        await Expect(Page.Locator("#ui-select-searchable")).ToBeFocusedAsync();
    });

    [Fact]
    public Task The_multiple_listbox_stays_open_and_counts_its_answers() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-multiselect']");
        var box = Page.Locator("#ui-select-multiple");

        await box.ClickAsync();
        await scope.Locator("[data-ui-option]", new LocatorLocatorOptions { HasTextString = "Design services" }).ClickAsync();
        await scope.Locator("[data-ui-option]", new LocatorLocatorOptions { HasTextString = "Other" }).ClickAsync();

        await Expect(scope.Locator("[role='listbox']")).ToBeVisibleAsync();
        await Expect(scope.Locator("[role='listbox']")).ToHaveAttributeAsync("aria-multiselectable", "true");
        await Expect(box).ToContainTextAsync("2 selected");
        await Expect(Page.Locator("[data-testid='ui-multiselect-state']")).ToContainTextAsync("Design services, Other");
    });

    [Fact]
    public Task The_combobox_filters_and_shows_its_answer() => RunAsync(async () =>
    {
        await OpenAsync();
        var input = Page.Locator("#ui-select-combobox");

        await input.ClickAsync();
        await Page.Keyboard.TypeAsync("de");
        await Expect(Page.Locator("[data-testid='ui-combobox'] [data-ui-option]:visible")).ToHaveCountAsync(2);
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(input).ToHaveValueAsync("Web development");
        await Expect(Page.Locator("[data-testid='ui-combobox-state']")).ToContainTextAsync("Industry: Web development.");
    });

    [Fact]
    public Task The_autocomplete_filters_as_it_is_typed_into_and_writes_the_pick_into_itself() => RunAsync(async () =>
    {
        await OpenAsync();
        var input = Page.Locator("#ui-autocomplete-state");
        var items = Page.Locator("[data-testid='ui-autocomplete'] [data-ui-autocomplete-item]:visible");

        await input.ClickAsync();
        await Page.Keyboard.TypeAsync("ne");
        await Expect(items).ToHaveCountAsync(10);
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(input).ToHaveValueAsync("Maine");
        await Expect(input).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(Page.Locator("[data-testid='ui-autocomplete-value']")).ToContainTextAsync("State: Maine.");
    });

    [Fact]
    public Task Escape_empties_the_autocomplete_and_text_that_is_no_item_stays_when_it_is_left() => RunAsync(async () =>
    {
        await OpenAsync();
        var input = Page.Locator("#ui-autocomplete-state");
        var state = Page.Locator("[data-testid='ui-autocomplete-value']");

        await input.ClickAsync();
        await Page.Keyboard.TypeAsync("Tex");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(input).ToHaveValueAsync("");
        await Page.Keyboard.TypeAsync("Atlantis");
        await Page.Keyboard.PressAsync("Tab");

        await Expect(input).ToHaveValueAsync("Atlantis");
        await Expect(state).ToContainTextAsync("State: Atlantis.");
    });

    [Fact]
    public Task The_pillbox_shows_each_pick_as_a_pill_and_takes_it_off_again() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-pillbox']");
        var trigger = Page.Locator("#ui-pillbox-tags");
        var pills = trigger.Locator("[data-value]");

        await trigger.ClickAsync();
        await scope.Locator("[data-ui-listbox-option]:visible", new LocatorLocatorOptions { HasTextString = "Sales" }).ClickAsync();
        await scope.Locator("[data-ui-listbox-option]:visible", new LocatorLocatorOptions { HasTextString = "Design" }).ClickAsync();
        await Expect(pills).ToHaveTextAsync(["Sales", "Design"]);
        await Expect(Page.Locator("[data-testid='ui-pillbox-state']")).ToContainTextAsync("Tags: Sales, Design.");
        await Page.Keyboard.PressAsync("Escape");
        await pills.First.Locator("> div").Last.ClickAsync();

        await Expect(pills).ToHaveTextAsync(["Design"]);
        await Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");
    });

    [Fact]
    public Task The_pillbox_opens_from_the_keyboard_without_the_page_moving_behind_it() => RunAsync(async () =>
    {
        await OpenAsync();
        var trigger = Page.Locator("#ui-pillbox-tags");

        // The trigger is no button: Space and the arrows open it in C#, and the runtime keeps them from the page.
        await trigger.FocusAsync();
        var before = (await trigger.BoundingBoxAsync())!.Y;
        await Page.Keyboard.PressAsync("Space");
        await Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");

        Assert.Equal(before, (await trigger.BoundingBoxAsync())!.Y);
    });

    [Fact]
    public Task The_searchable_pillbox_filters_from_its_search_field() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-pillbox']");
        var search = scope.Locator("[data-ui-pillbox-search] input");

        await Page.Locator("#ui-pillbox-searchable").ClickAsync();
        await Expect(search).ToBeFocusedAsync();
        await Expect(search).ToHaveAttributeAsync("placeholder", "Filter skills...");
        await Page.Keyboard.TypeAsync("p");
        await Expect(scope.Locator("[data-ui-options] [data-ui-listbox-option]:visible")).ToHaveCountAsync(4);
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(search).ToHaveValueAsync("");
        await Expect(Page.Locator("[data-testid='ui-pillbox-state']")).ToContainTextAsync("Skills: TypeScript.");
    });

    [Fact]
    public Task The_combobox_pillbox_is_typed_into_among_its_pills() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-pillbox-combobox']");
        var input = Page.Locator("#ui-pillbox-combobox");
        var state = Page.Locator("[data-testid='ui-pillbox-combobox-state']");

        await input.ClickAsync();
        await Page.Keyboard.TypeAsync("ru");
        await Expect(scope.Locator("[data-ui-listbox-option]:visible")).ToHaveCountAsync(2);
        await Page.Keyboard.PressAsync("Enter");
        await Expect(input).ToHaveValueAsync("");
        await Expect(state).ToContainTextAsync("Skills: Ruby.");
        await Page.Keyboard.PressAsync("Backspace");

        await Expect(state).ToContainTextAsync("Skills: none.");
    });

    [Fact]
    public Task The_pillbox_creates_the_tag_that_is_not_there() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-pillbox-combobox']");
        var input = Page.Locator("#ui-pillbox-create");

        await input.ClickAsync();
        await Page.Keyboard.TypeAsync("Research");
        await Expect(scope.Locator("[data-ui-option-create]:visible")).ToContainTextAsync("Create new \"Research\"");
        await scope.Locator("[data-ui-option-create]:visible").ClickAsync();

        await Expect(input).ToHaveValueAsync("");
        await Expect(Page.Locator("[data-testid='ui-pillbox-combobox-state']")).ToContainTextAsync("Tags: Research.");
    });

    [Fact]
    public Task The_combobox_creates_the_option_that_is_not_there() => RunAsync(async () =>
    {
        await OpenAsync();
        var input = Page.Locator("#ui-select-create");

        await input.ClickAsync();
        await Page.Keyboard.TypeAsync("Onboarding");
        await Expect(Page.Locator("[data-testid='ui-combobox'] [data-ui-option-create]:visible")).ToHaveCountAsync(1);
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page.Locator("[data-testid='ui-combobox-state']")).ToContainTextAsync("Project: Onboarding.");
    });

    [Fact]
    public Task A_range_is_written_only_once_it_has_both_ends() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dates']");
        var calendar = scope.GetByRole(AriaRole.Group, new LocatorGetByRoleOptions { Name = "Stay", Exact = true });
        await calendar.ScrollIntoViewIfNeededAsync();
        var days = calendar.Locator("tbody button:not([disabled])");
        var state = Page.Locator("[data-testid='ui-dates-stay']");

        // The later day first. The first click is drawn but written nowhere: the page still has no stay.
        await days.Nth(14).ClickAsync();
        await Expect(days.Nth(14)).ToHaveAttributeAsync("aria-pressed", "true",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await Expect(state).ToHaveTextAsync("No stay chosen.");

        await days.Nth(9).ClickAsync();
        await Expect(state).ToContainTextAsync("-10 to ", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(state).ToContainTextAsync("-15");
    });

    [Fact]
    public Task A_picker_opens_the_grid_and_closes_on_the_pick() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dates']");
        var field = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Arrival" });
        await field.ScrollIntoViewIfNeededAsync();
        await field.ClickAsync();

        var dialog = scope.GetByRole(AriaRole.Dialog, new LocatorGetByRoleOptions { Name = "Arrival" });
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Expect(field).ToHaveAttributeAsync("aria-expanded", "true");

        await dialog.Locator("tbody button:not([disabled])").Nth(4).ClickAsync();

        // Closed by the same click, the choice reached C#, and the field shows it.
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(Page.Locator("[data-testid='ui-dates-picked']")).ToContainTextAsync("Arrival: ",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(field).Not.ToContainTextAsync("Choose a date");
    });

    [Fact]
    public Task Several_days_keep_the_picker_open() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dates']");
        var field = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Days off" });
        await field.ScrollIntoViewIfNeededAsync();
        await field.ClickAsync();

        var dialog = scope.GetByRole(AriaRole.Dialog, new LocatorGetByRoleOptions { Name = "Days off" });
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        var days = dialog.Locator("tbody button:not([disabled])");

        await days.Nth(2).ClickAsync();
        await Expect(days.Nth(2)).ToHaveAttributeAsync("aria-pressed", "true",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await days.Nth(5).ClickAsync();

        await Expect(Page.Locator("[data-testid='ui-dates-picked']")).ToContainTextAsync("2 days off",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(dialog).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
    });

    [Fact]
    public Task The_drop_area_is_the_native_input_and_lights_up_under_a_dragged_file() => RunAsync(async () =>
    {
        await OpenAsync();

        var zone = Page.Locator("[data-testid='ui-dropzone'] [data-rask-dropzone]");
        await Expect(zone).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await zone.ScrollIntoViewIfNeededAsync();

        // No script routes a drop: the input IS the area. What is under the middle of the area — and under a
        // corner of it — is the file input itself, so a real click or a real drop lands there.
        var hits = await zone.EvaluateAsync<string[]>(
            @"z => { const r = z.getBoundingClientRect();
                     return [[r.left + r.width / 2, r.top + r.height / 2], [r.left + 4, r.top + 4]]
                       .map(([x, y]) => { const el = document.elementFromPoint(x, y);
                                          return el ? el.tagName + ':' + el.getAttribute('type') : 'none'; }); }");
        Assert.All(hits, hit => Assert.Equal("INPUT:file", hit));

        // A chosen file reaches C# through OnFiles, same as the compact box.
        await zone.Locator("input[type=file]").SetInputFilesAsync(new[]
        {
            new FilePayload { Name = "march.pdf", MimeType = "application/pdf", Buffer = [1, 2, 3] },
            new FilePayload { Name = "april.jpg", MimeType = "image/jpeg", Buffer = [4, 5, 6] },
        });
        await Expect(Page.Locator("[data-testid='ui-dropzone-state']")).ToHaveTextAsync(
            "Chosen: march.pdf, april.jpg", new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });

        // The highlight is the runtime's: counted across the children a drag crosses, and only for FILES.
        await Page.EvaluateAsync(
            @"() => { const zone = document.querySelector('[data-testid=ui-dropzone] [data-rask-dropzone]');
                      const input = zone.querySelector('input');
                      const files = new DataTransfer(); files.items.add(new File(['x'], 'x.pdf'));
                      const fire = (type, el, dt) => el.dispatchEvent(new DragEvent(type, {bubbles: true, dataTransfer: dt}));
                      window.__dz = [];
                      fire('dragenter', zone, files); fire('dragenter', input, files);
                      window.__dz.push(zone.hasAttribute('data-dragging'));
                      fire('dragleave', zone, files);
                      window.__dz.push(zone.hasAttribute('data-dragging'));
                      fire('dragleave', input, files);
                      window.__dz.push(zone.hasAttribute('data-dragging'));
                      const text = new DataTransfer(); text.setData('text/plain', 'row');
                      fire('dragenter', input, text);
                      window.__dz.push(zone.hasAttribute('data-dragging')); }");
        var marks = await Page.EvaluateAsync<bool[]>("() => window.__dz");
        // In; still in after crossing out of one child; out once the last one is left; never for a text drag.
        Assert.Equal(new[] { true, true, false, false }, marks);
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Data input");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data input",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }

    private ILocator OtpCells(int example) =>
        Page.Locator("[data-testid='ui-otp'] [data-ui-otp]").Nth(example).Locator("input[data-ui-otp-input]");

    // The cells as text, an underscore for an empty one: "1 9 3 _ _ _".
    private Task<string> OtpTextAsync(int example) =>
        Page.Locator("[data-testid='ui-otp'] [data-ui-otp]").Nth(example).EvaluateAsync<string>(
            "group => [...group.querySelectorAll('input[data-ui-otp-input]')].map(cell => cell.value || '_').join(' ')");
}
