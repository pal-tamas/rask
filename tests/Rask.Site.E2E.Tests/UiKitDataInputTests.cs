using System.Globalization;
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
                     "ui-text-controls", "ui-input-group", "ui-textarea", "ui-select", "ui-listbox", "ui-select-search", "ui-combobox", "ui-autocomplete", "ui-pillbox", "ui-pillbox-combobox", "ui-checkbox", "ui-radio", "ui-switch", "ui-range", "ui-otp", "ui-filter",
                     "ui-calendar", "ui-date-picker", "ui-time-picker", "ui-dropzone", "ui-bound", "ui-mask",
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
    public Task The_one_time_code_is_a_single_field_that_takes_a_pasted_code() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-otp']");
        var field = scope.Locator("input.otp");

        // One input, not six. This is what makes a pasted or autofilled code land correctly instead of
        // dropping the whole string into the first box.
        await Expect(scope.Locator("input")).ToHaveCountAsync(1);

        // Named by its visible label and described by its hint, like every other kit field (#1117).
        await Expect(scope.GetByLabel("Verification code")).ToHaveCountAsync(1);
        await Expect(field).ToHaveAccessibleDescriptionAsync("Six digits, sent to your phone.");

        await field.FillAsync("123456");
        await field.BlurAsync();
        await Expect(Page.Locator("[data-testid='ui-otp-state']")).ToContainTextAsync("Code complete");
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

        var scope = Page.Locator("[data-testid='ui-calendar'] [data-ui-calendar]").First;
        var state = Page.Locator("[data-testid='ui-calendar-state']");
        await scope.ScrollIntoViewIfNeededAsync();

        await Expect(state).ToContainTextAsync("No date chosen");

        // Paging months is an ordinary re-render — there is no web component here to ask.
        var first = scope.Locator("td[data-date]:not([data-outside])").First;
        var before = await first.GetAttributeAsync("data-date");
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Next month" })
            .ClickAsync();
        await Expect(first).Not.ToHaveAttributeAsync("data-date", before ?? "",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });

        await first.Locator("button").ClickAsync();
        await Expect(state).ToContainTextAsync("Chosen:", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(first).ToHaveAttributeAsync("aria-selected", "true");
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
    public Task The_range_reports_its_value() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-range']");

        await Expect(scope).ToContainTextAsync("Volume: 40");

        // A slider that draws a value and reports nothing is one you can push and cannot read; Ui.Range
        // had no OnChange at all before this.
        await scope.Locator("input[type='range']").FillAsync("75");
        await Expect(scope).ToContainTextAsync("Volume: 75");
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

    // No wait between the keys: the page is still answering "Tex" when Escape and the next text arrive. Escape
    // used to empty the input from its handler, a round trip late, and this ended as "TexAtlantis".
    [Fact]
    public Task Text_typed_straight_after_Escape_is_all_the_autocomplete_holds() => RunAsync(async () =>
    {
        await OpenAsync();
        var input = Page.Locator("#ui-autocomplete-state");

        await input.ClickAsync();
        await Page.Keyboard.TypeAsync("Tex");
        await Page.Keyboard.PressAsync("Escape");
        await Page.Keyboard.TypeAsync("Atlantis");
        await Page.Keyboard.PressAsync("Tab");

        await Expect(input).ToHaveValueAsync("Atlantis");
        await Expect(Page.Locator("[data-testid='ui-autocomplete-value']")).ToContainTextAsync("State: Atlantis.");
        await Expect(input).ToHaveAttributeAsync("aria-expanded", "false");
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

        var calendar = Page.Locator("[data-testid='ui-calendar'] [data-ui-calendar]").Nth(2);
        await calendar.ScrollIntoViewIfNeededAsync();
        var days = calendar.Locator("[data-month]").First.Locator("td[data-date]:not([data-outside])");
        var state = Page.Locator("[data-testid='ui-calendar-stay']");

        // The first click is drawn as the start and written nowhere: the page still has no stay.
        await days.Nth(9).Locator("button").ClickAsync();
        await Expect(days.Nth(9)).ToHaveAttributeAsync("data-start", "",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await Expect(state).ToHaveTextAsync("No stay chosen.");

        await days.Nth(14).Locator("button").ClickAsync();
        await Expect(state).ToContainTextAsync("-10 to ", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(state).ToContainTextAsync("-15");
        await Expect(days.Nth(12)).ToHaveAttributeAsync("data-in-range", "");
    });

    [Fact]
    public Task A_picker_opens_the_grid_and_closes_on_the_pick() => RunAsync(async () =>
    {
        await OpenAsync();

        var picker = Page.Locator("[data-testid='ui-date-picker'] [data-ui-date-picker]").First;
        var field = picker.Locator("[data-ui-date-picker-button]");
        await field.ScrollIntoViewIfNeededAsync();
        await Expect(field).ToContainTextAsync("Select a date");
        await field.ClickAsync();

        var dialog = picker.Locator("dialog");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Expect(field).ToHaveAttributeAsync("aria-expanded", "true");

        await dialog.Locator("td[data-date]:not([data-outside]) button:not([disabled])").Nth(4).ClickAsync();

        // Closed by the same click, the choice reached C#, and the field shows it.
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(Page.Locator("[data-testid='ui-date-picker-state']")).ToContainTextAsync("Arrival: ",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(field).Not.ToContainTextAsync("Select a date");
    });

    [Fact]
    public Task A_preset_writes_its_range_and_closes_the_picker() => RunAsync(async () =>
    {
        await OpenAsync();

        var picker = Page.Locator("[data-testid='ui-date-picker'] [data-ui-date-picker]").Nth(6);
        var field = picker.Locator("[data-ui-date-picker-button]");
        await field.ScrollIntoViewIfNeededAsync();
        await field.ClickAsync();

        var dialog = picker.Locator("dialog");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await dialog.GetByRole(AriaRole.Radio, new LocatorGetByRoleOptions { Name = "Today", Exact = true }).ClickAsync();

        await Expect(dialog).ToBeHiddenAsync();
        await Expect(field).ToContainTextAsync("Today", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("[data-testid='ui-date-picker-range']")).ToContainTextAsync("Range: ");
    });

    [Fact]
    public Task A_time_picker_opens_its_list_and_closes_on_the_pick() => RunAsync(async () =>
    {
        await OpenAsync();

        var picker = Page.Locator("[data-testid='ui-time-picker'] [data-ui-time-picker]").First;
        var field = picker.GetByRole(AriaRole.Combobox);
        await field.ScrollIntoViewIfNeededAsync();
        await field.ClickAsync();

        var list = picker.GetByRole(AriaRole.Listbox);
        await Expect(list).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        await list.Locator("[data-time='09:30']").ClickAsync();

        await Expect(list).ToBeHiddenAsync();
        await Expect(Page.Locator("[data-testid='ui-time-picker-state']")).ToHaveTextAsync("Chosen: 09:30",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
    });

    [Fact]
    public Task The_arrows_carry_the_focus_through_the_calendar_and_the_page_does_not_scroll() => RunAsync(async () =>
    {
        await OpenAsync();

        var calendar = Page.Locator("[data-testid='ui-calendar'] [data-ui-calendar]").First;
        await calendar.ScrollIntoViewIfNeededAsync();
        await calendar.Locator("td button[tabindex='0']").FocusAsync();
        var start = DateOnly.ParseExact(await Page.EvaluateAsync<string>(FocusedDay), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var scrolled = await Page.EvaluateAsync<double>("() => scrollY");

        // A day on, then five weeks down — into another month whatever today is. After each key the BROWSER's
        // focus is on that day's button, which is what the next key is pressed on.
        await Page.Keyboard.PressAsync("ArrowRight");
        await FocusIsOnAsync(start.AddDays(1));
        for (var week = 1; week <= 5; week++)
        {
            await Page.Keyboard.PressAsync("ArrowDown");
            await FocusIsOnAsync(start.AddDays(1 + (7 * week)));
        }

        // A paging key pages a month and lets go of the focus, as Flux UI's calendar does.
        await Page.Keyboard.PressAsync("PageDown");
        await Page.WaitForFunctionAsync("() => document.activeElement === document.body", null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        // The tab stop is named again once the focus has fallen: an arrow pressed on it carries the focus on.
        var stop = calendar.Locator("td button[tabindex='0'][data-rask-focus-target]");
        await Expect(stop).ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await stop.FocusAsync();
        var paged = DateOnly.ParseExact(await Page.EvaluateAsync<string>(FocusedDay), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        await Page.Keyboard.PressAsync("ArrowLeft");
        await FocusIsOnAsync(paged.AddDays(-1));

        // None of it scrolled the page behind the grid.
        Assert.Equal(scrolled, await Page.EvaluateAsync<double>("() => scrollY"));
    });

    [Fact]
    public Task A_date_typed_into_the_fields_is_committed_and_only_the_room_beside_them_opens_the_calendar() => RunAsync(async () =>
    {
        await OpenAsync();

        var picker = Page.Locator("[data-testid='ui-date-picker'] [data-ui-date-picker]").Nth(1);
        var dialog = picker.Locator("dialog");
        await picker.ScrollIntoViewIfNeededAsync();

        // A press in a field puts the caret there and opens nothing. 3 can only be March, so it moves on.
        await picker.Locator("[data-rask-segment='month']").ClickAsync();
        await Page.Keyboard.TypeAsync("3152027", new KeyboardTypeOptions { Delay = 60 });

        await Expect(Page.Locator("[data-testid='ui-date-picker-state']")).ToHaveTextAsync("Arrival: 2027-03-15",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(picker.Locator("[data-rask-segment='month']")).ToHaveValueAsync("03");
        await Expect(picker.Locator("[data-rask-segment='year']")).ToHaveValueAsync("2027");
        await Expect(dialog).ToBeHiddenAsync();

        // The chevron beside the fields opens the calendar, on the month that was typed.
        await picker.Locator("[data-ui-group-target] > svg").Last.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Expect(dialog.Locator("[data-month]").First).ToContainTextAsync("March 2027");
        await Expect(dialog.Locator("td[data-date='2027-03-15']")).ToHaveAttributeAsync("aria-selected", "true");
    });

    [Fact]
    public Task An_arrow_in_the_presets_chooses_the_next_one_which_closes_the_picker() => RunAsync(async () =>
    {
        await OpenAsync();

        var picker = Page.Locator("[data-testid='ui-date-picker'] [data-ui-date-picker]").Nth(6);
        var field = picker.Locator("[data-ui-date-picker-button]");
        var dialog = picker.Locator("dialog");
        await field.ScrollIntoViewIfNeededAsync();
        await field.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // The page behind the open picker neither scrolls nor takes the pointer, as under Flux's.
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-rask-locked", "all");

        // The tab stop is the first preset while none is chosen; the arrow walks to the next and chooses it, as
        // a radio group does — and a chosen preset closes Flux's picker and hands the focus back to its button.
        await dialog.Locator("[role='radio'][tabindex='0']").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowDown");

        await Expect(dialog).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15_000 });
        await Expect(field).ToContainTextAsync("Yesterday", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(field).ToBeFocusedAsync();
        await Expect(dialog.Locator("[role='radio'][value='yesterday']")).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(Page.Locator("html")).Not.ToHaveAttributeAsync("data-rask-locked", "all");
    });

    [Fact]
    public Task The_time_list_follows_the_keyboard_inside_itself_and_a_typed_time_is_committed() => RunAsync(async () =>
    {
        await OpenAsync();

        var picker = Page.Locator("[data-testid='ui-time-picker'] [data-ui-time-picker]").First;
        var field = picker.GetByRole(AriaRole.Combobox);
        var list = picker.GetByRole(AriaRole.Listbox);
        await field.ScrollIntoViewIfNeededAsync();
        await field.FocusAsync();

        // Closed, Enter does nothing — Flux's button, not a native one — and an arrow opens the list.
        await Page.Keyboard.PressAsync("Enter");
        await Expect(list).ToBeHiddenAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(list).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        var scrolled = await Page.EvaluateAsync<double>("() => scrollY");

        // Past the visible window: the list scrolls to keep the active time in view, the page stays, and the
        // button keeps the focus.
        for (var step = 0; step < 14; step++)
        {
            await Page.Keyboard.PressAsync("ArrowDown");
        }

        await Page.WaitForFunctionAsync("list => list.scrollTop > 0", await list.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        await Expect(field).ToBeFocusedAsync();
        Assert.Equal(scrolled, await Page.EvaluateAsync<double>("() => scrollY"));
        await Page.Keyboard.PressAsync("Escape");

        // The typed trigger: 9 can only be nine o'clock, so the hour moves on; `p` makes it the afternoon.
        var typed = Page.Locator("[data-testid='ui-time-picker'] [data-ui-time-picker]").Nth(1);
        await typed.ScrollIntoViewIfNeededAsync();
        await typed.Locator("[data-rask-segment='hour']").ClickAsync();
        await Page.Keyboard.TypeAsync("930p", new KeyboardTypeOptions { Delay = 60 });

        await Expect(typed.Locator("input[type='hidden']")).ToHaveValueAsync("21:30",
            new LocatorAssertionsToHaveValueOptions { Timeout = 15_000 });
        await Expect(typed.Locator("[popover]")).ToBeHiddenAsync();
    });

    private const string FocusedDay = "() => document.activeElement.closest('[data-date]')?.getAttribute('data-date') ?? ''";

    private Task FocusIsOnAsync(DateOnly day) =>
        Page.WaitForFunctionAsync(
            "day => document.activeElement.closest('[data-date]')?.getAttribute('data-date') === day",
            day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            new PageWaitForFunctionOptions { Timeout = 15_000 });

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
}
