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
                     "ui-calendar", "ui-dates", "ui-file-upload", "ui-bound", "ui-mask",
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
    public Task A_file_upload_takes_chosen_files_lists_them_and_removes_one() => RunAsync(async () =>
    {
        await OpenAsync();
        var basic = Page.Locator("[data-testid='ui-upload-basic']");
        var upload = basic.Locator("[data-ui-file-upload]");
        await upload.ScrollIntoViewIfNeededAsync();

        // A click anywhere on the area opens the picker: the area is a <label> around the input.
        var chooser = await Page.RunAndWaitForFileChooserAsync(
            () => upload.Locator("[data-ui-file-upload-dropzone]").ClickAsync());
        Assert.True(chooser.IsMultiple);
        await chooser.SetFilesAsync(new[]
        {
            new FilePayload { Name = "march.png", MimeType = "image/png", Buffer = [1, 2, 3] },
            new FilePayload { Name = "april.jpg", MimeType = "image/jpeg", Buffer = new byte[2048] },
        });

        // OnFiles handed the page both; it drew an item for each under the one that was there, with its size.
        var items = basic.Locator("[data-ui-file-item]");
        await Expect(items).ToHaveCountAsync(3, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(items.Nth(0)).ToContainTextAsync("159 KB");
        await Expect(items.Nth(0).Locator("[data-slot='image'] img")).ToBeVisibleAsync();
        await Expect(items.Nth(1)).ToContainTextAsync("march.png");
        await Expect(items.Nth(1)).ToContainTextAsync("3 B");
        await Expect(items.Nth(2)).ToContainTextAsync("2 KB");

        await basic.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove file: march.png" })
            .ClickAsync();
        await Expect(items).ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(items.Nth(1)).ToContainTextAsync("april.jpg");
    });

    [Fact]
    public Task A_file_upload_is_reached_by_keyboard_and_rings_its_dropzone() => RunAsync(async () =>
    {
        await OpenAsync();
        var upload = Page.Locator("[data-testid='ui-upload-inline'] [data-ui-file-upload]");
        await upload.ScrollIntoViewIfNeededAsync();

        // The real input holds the focus, out of sight; the ring is drawn on the dropzone beside it.
        await upload.Locator("input[type=file]").FocusAsync();
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Page.Keyboard.PressAsync("Tab");
        await Expect(upload.Locator("input[type=file]")).ToBeFocusedAsync();
        var outline = await upload.Locator("[data-ui-file-upload-dropzone]")
            .EvaluateAsync<string>("z => getComputedStyle(z).outlineStyle");
        Assert.Equal("auto", outline);

        var chooser = await Page.RunAndWaitForFileChooserAsync(() => Page.Keyboard.PressAsync("Space"));
        Assert.True(chooser.IsMultiple);
    });

    [Fact]
    public Task A_file_dragged_over_an_upload_marks_it_and_puts_the_input_under_the_pointer() => RunAsync(async () =>
    {
        await OpenAsync();
        var upload = Page.Locator("[data-testid='ui-upload-inline'] [data-ui-file-upload]");
        await upload.ScrollIntoViewIfNeededAsync();

        // At rest what is under the pointer is the dropzone; the input is a pixel, out of sight.
        Assert.NotEqual("INPUT", await UnderTheMiddleOf(upload));
        var dropzone = upload.Locator("[data-ui-file-upload-dropzone]");
        var atRest = await dropzone.EvaluateAsync<string[]>("z => [getComputedStyle(z).backgroundColor, getComputedStyle(z).borderTopColor]");

        // The runtime marks the area for a drag carrying FILES, and the input is then laid over all of it —
        // so the browser's own drop puts the files in it, and no script routes them.
        await upload.EvaluateAsync(
            @"zone => { const files = new DataTransfer(); files.items.add(new File(['x'], 'x.pdf'));
                        zone.querySelector('[data-ui-file-upload-dropzone]')
                            .dispatchEvent(new DragEvent('dragenter', {bubbles: true, dataTransfer: files})); }");
        await Expect(upload).ToHaveAttributeAsync("data-dragging", "");
        Assert.Equal("INPUT", await UnderTheMiddleOf(upload));

        // And it looks dragged over: the dropzone's fill and border darken, in the app's ONE sheet.
        await dropzone.EvaluateAsync("z => Promise.all(z.getAnimations().map(a => a.finished))");
        var draggedOver = await dropzone.EvaluateAsync<string[]>("z => [getComputedStyle(z).backgroundColor, getComputedStyle(z).borderTopColor]");
        Assert.NotEqual(atRest[0], draggedOver[0]);
        Assert.NotEqual(atRest[1], draggedOver[1]);

        await upload.EvaluateAsync(
            @"zone => { const files = new DataTransfer(); files.items.add(new File(['x'], 'x.pdf'));
                        zone.querySelector('[data-ui-file-upload-dropzone]')
                            .dispatchEvent(new DragEvent('dragleave', {bubbles: true, dataTransfer: files})); }");
        Assert.Null(await upload.GetAttributeAsync("data-dragging"));

        // A row of a sortable list dragged across is not an offer to upload it.
        await upload.EvaluateAsync(
            @"zone => { const text = new DataTransfer(); text.setData('text/plain', 'row');
                        zone.dispatchEvent(new DragEvent('dragenter', {bubbles: true, dataTransfer: text})); }");
        Assert.Null(await upload.GetAttributeAsync("data-dragging"));
    });

    [Fact]
    public Task A_dropped_file_reaches_the_page_like_a_chosen_one() => RunAsync(async () =>
    {
        await OpenAsync();
        var inline = Page.Locator("[data-testid='ui-upload-inline']");
        var upload = inline.Locator("[data-ui-file-upload]");
        await upload.ScrollIntoViewIfNeededAsync();
        var path = Path.Combine(Path.GetTempPath(), $"rask-drop-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "hello");

        try
        {
            // The browser's own drag pipeline, not a synthetic event: only that fills an input on a drop.
            var box = (await upload.BoundingBoxAsync())!;
            var (edgeX, edgeY) = (box.X + 6, box.Y + 6);
            var (midX, midY) = (box.X + (box.Width / 2), box.Y + (box.Height / 2));
            var cdp = await Page.Context.NewCDPSessionAsync(Page);
            foreach (var (type, x, y) in new[]
                     {
                         ("dragEnter", edgeX, edgeY), ("dragOver", edgeX, edgeY), ("dragOver", midX, midY),
                         ("drop", midX, midY),
                     })
            {
                await cdp.SendAsync("Input.dispatchDragEvent", new Dictionary<string, object>
                {
                    ["type"] = type,
                    ["x"] = x,
                    ["y"] = y,
                    ["data"] = new Dictionary<string, object>
                    {
                        ["items"] = Array.Empty<object>(),
                        ["files"] = new[] { path },
                        ["dragOperationsMask"] = 1,
                    },
                });
            }

            await Expect(inline.Locator("[data-ui-file-item]")).ToContainTextAsync(
                Path.GetFileName(path), new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
            Assert.Null(await upload.GetAttributeAsync("data-dragging"));
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public Task A_disabled_upload_opens_nothing_and_a_custom_one_takes_a_file_all_the_same() => RunAsync(async () =>
    {
        await OpenAsync();
        var disabled = Page.Locator("[data-testid='ui-upload-disabled'] [data-ui-file-upload]");
        await disabled.ScrollIntoViewIfNeededAsync();

        await Expect(disabled.Locator("input[type=file]")).ToBeDisabledAsync();
        Assert.Equal("none", await disabled.Locator("[data-ui-file-upload-dropzone]")
            .EvaluateAsync<string>("z => getComputedStyle(z).pointerEvents"));

        // Markup of the page's own in place of the dropzone: the same input behind it.
        var custom = Page.Locator("[data-testid='ui-upload-custom']");
        await custom.Locator("input[type=file]").SetInputFilesAsync(
            new FilePayload { Name = "me.png", MimeType = "image/png", Buffer = [1, 2, 3] });
        await Expect(custom).ToContainTextAsync(
            "Chosen: me.png", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    });

    [Fact]
    public Task A_chosen_picture_is_read_in_the_browser_and_shown_as_its_own_preview() => RunAsync(async () =>
    {
        await OpenAsync();
        var basic = Page.Locator("[data-testid='ui-upload-basic']");
        await basic.Locator("[data-ui-file-upload]").ScrollIntoViewIfNeededAsync();

        // A real picture, one pixel square: the page reads its bytes through OpenReadStream (#1200).
        await basic.Locator("input[type=file]").SetInputFilesAsync(
            new FilePayload { Name = "dot.png", MimeType = "image/png", Buffer = Convert.FromBase64String(OnePixelPng) });

        var preview = basic.Locator("[data-ui-file-item]").Nth(1).Locator("[data-slot='image'] img");
        await Expect(preview).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        Assert.Equal("data:image/png;base64," + OnePixelPng, await preview.GetAttributeAsync("src"));
        await Expect(preview).ToHaveJSPropertyAsync("naturalWidth", 1);
    });

    [Fact]
    public Task An_upload_says_how_far_its_files_have_been_read_and_rests_once_the_page_has_them() => RunAsync(async () =>
    {
        await OpenAsync();
        var progress = Page.Locator("[data-testid='ui-upload-progress']");
        var upload = progress.Locator("[data-ui-file-upload]");
        await upload.ScrollIntoViewIfNeededAsync();
        var atRest = await upload.EvaluateAsync<string>("u => getComputedStyle(u).getPropertyValue('--ui-file-upload-progress')");
        await upload.EvaluateAsync(
            @"u => { window.seen = [];
                     const note = () => window.seen.push((u.hasAttribute('data-loading') ? 'loading' : 'idle') + ' '
                         + u.style.getPropertyValue('--rask-progress') + ' '
                         + getComputedStyle(u.querySelector('[data-ui-file-upload-dropzone]')).getPropertyValue('--ui-file-upload-progress'));
                     new MutationObserver(note).observe(u, { attributes: true }); }");

        await upload.Locator("input[type=file]").SetInputFilesAsync(
            new FilePayload { Name = "film.bin", MimeType = "application/octet-stream", Buffer = new byte[6_000_000] });
        await Expect(progress.Locator("[data-ui-file-item]")).ToContainTextAsync(
            "film.bin", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Expect(upload).Not.ToHaveAttributeAsync("data-loading", "");
        var seen = await Page.EvaluateAsync<string[]>("() => window.seen");

        // Marked at once, at nothing; then what the handler has read, which the dropzone's bar takes its width from.
        Assert.Equal("0%", atRest.Trim());
        Assert.Contains("loading 0% 0%", seen);
        Assert.Contains("loading 100% 100%", seen);
        Assert.Contains(seen, s => Between(s) is > 0 and < 100);
        Assert.Equal(string.Empty, await upload.EvaluateAsync<string>("u => u.style.getPropertyValue('--rask-progress')"));
    });

    // A 1 × 1 PNG.
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    // The whole percent of a note the observer took while loading, or -1.
    private static int Between(string note)
    {
        var parts = note.Split(' ');
        return parts is ["loading", var percent, ..] && int.TryParse(percent.TrimEnd('%'), out var value) ? value : -1;
    }

    private static Task<string> UnderTheMiddleOf(ILocator area) =>
        area.EvaluateAsync<string>(
            @"a => { const r = a.getBoundingClientRect();
                     return document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2).tagName; }");

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
