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
                     "ui-composer", "ui-toggle", "ui-autocomplete", "ui-pillbox", "ui-pillbox-combobox", "ui-phone",
                     "ui-calendar", "ui-date-picker", "ui-time-picker", "ui-color-picker", "ui-file-upload",
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
    public Task A_composer_grows_as_lines_are_typed_and_sends_its_form_on_Ctrl_Enter() => RunAsync(async () =>
    {
        await OpenAsync();
        var prompt = Page.Locator("#composer-prompt");
        await prompt.FocusAsync();
        var before = (await prompt.BoundingBoxAsync())!.Height;
        await Page.Keyboard.TypeAsync("What is Rask?");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.TypeAsync("Briefly.");

        await Page.Keyboard.PressAsync("Control+Enter");

        await Expect(Page.Locator("[data-testid='ui-composer-sent']")).ToContainTextAsync("Sent: What is Rask?");
        Assert.True((await prompt.BoundingBoxAsync())!.Height > before, "the composer did not grow with its third line.");
    });

    [Fact]
    public Task A_composer_that_sends_on_Enter_keeps_Shift_Enter_for_a_new_line() => RunAsync(async () =>
    {
        await OpenAsync();
        var quick = Page.Locator("#composer-enter");
        await quick.FocusAsync();
        await Page.Keyboard.TypeAsync("One");
        await Page.Keyboard.PressAsync("Shift+Enter");
        await Page.Keyboard.TypeAsync("Two");

        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page.Locator("[data-testid='ui-composer-sent']")).ToContainTextAsync("Sent: One Two");
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
    public Task A_pillbox_search_that_waits_for_a_pause_asks_the_store_once_and_adds_a_pill_from_its_answer() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-pillbox']");
        var state = Page.Locator("[data-testid='ui-pillbox-state']");

        await Page.Locator("#ui-pillbox-store").ClickAsync();
        var field = Page.Locator("#ui-pillbox-store").Locator("xpath=..").Locator("[data-ui-pillbox-search] input");
        await Expect(field).ToBeFocusedAsync();
        await field.FillAsync("script");
        await Expect(scope.Locator("[data-ui-listbox-option]:visible")).ToHaveTextAsync(["JavaScript", "TypeScript"]);
        await scope.Locator("[data-ui-listbox-option]:visible", new LocatorLocatorOptions { HasTextString = "TypeScript" }).ClickAsync();

        // The search for "script", and the empty one the pick left behind it.
        await Expect(state).ToContainTextAsync("Stored: TypeScript. Searches: 2.");
        await Expect(field).ToHaveValueAsync(string.Empty);
    });

    [Fact]
    public Task A_pillbox_input_that_waits_for_a_pause_asks_the_store_once_and_keeps_its_pills_under_the_next_search() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-pillbox-combobox']");
        var state = Page.Locator("[data-testid='ui-pillbox-combobox-state']");
        var input = Page.Locator("#ui-pillbox-backend");

        await input.ClickAsync();
        await input.FillAsync("des");
        await Expect(scope.Locator("[data-ui-listbox-option]:visible")).ToHaveTextAsync(["Design"]);
        await scope.Locator("[data-ui-listbox-option]:visible").ClickAsync();
        await Expect(state).ToContainTextAsync("Found: Design. Searches: 2.");
        // Back in the input, as a reader who goes on to type is: the press on the row left focus in the list.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("[data-ui-pillbox-trigger]:has(#ui-pillbox-backend)")).ToHaveAttributeAsync("aria-expanded", "false");
        await input.ClickAsync();
        await Expect(Page.Locator("[data-ui-pillbox-trigger]:has(#ui-pillbox-backend)")).ToHaveAttributeAsync("aria-expanded", "true");
        await input.FillAsync("sal");
        await Expect(scope.Locator("[data-ui-listbox-option]:visible")).ToHaveTextAsync(["Sales"]);

        // The answer no longer holds Design's option, and its pill stands all the same.
        await Expect(Page.Locator("[data-ui-pillbox-trigger]:has(#ui-pillbox-backend) [data-value]")).ToHaveTextAsync(["Design"]);
        await Expect(state).ToContainTextAsync("Found: Design. Searches: 3.");
    });

    [Fact]
    public Task An_autocomplete_that_waits_for_a_pause_asks_the_store_once_and_takes_the_item_pressed() => RunAsync(async () =>
    {
        await OpenAsync();
        var input = Page.Locator("#ui-autocomplete-city");
        var state = Page.Locator("[data-testid='ui-autocomplete-city-state']");

        await input.ClickAsync();
        await input.FillAsync("ber");
        await Expect(state).ToHaveTextAsync("City: ber. Searches: 1.");
        await Page.Locator("[data-testid='ui-autocomplete'] [data-ui-autocomplete-item]:visible", new PageLocatorOptions { HasTextString = "Bern" }).ClickAsync();

        await Expect(state).ToHaveTextAsync("City: Bern. Searches: 2.");
        await Expect(input).ToHaveValueAsync("Bern");
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
        // The pillbox itself: the section holds a second one with a search field, over a store.
        var scope = Page.Locator("[data-ui-pillbox]:has(#ui-pillbox-searchable)");
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
    public Task An_open_picker_holds_the_focus_with_no_ring_after_a_key() => RunAsync(async () =>
    {
        await OpenAsync();
        var picker = Page.Locator("[data-testid='ui-date-picker'] [data-ui-date-picker]").First;
        var field = picker.Locator("[data-ui-date-picker-button]");
        await field.ScrollIntoViewIfNeededAsync();
        await field.ClickAsync();
        var dialog = picker.Locator("dialog");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // A key makes the focus visible, as Tab into the form before it did on an app's page.
        await Page.Keyboard.PressAsync("Shift");

        await Expect(dialog).ToBeFocusedAsync();
        Assert.True(await dialog.EvaluateAsync<bool>("d => d.matches(':focus-visible')"));
        await Expect(dialog).ToHaveCSSAsync("outline-style", "none");
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
    public Task On_a_phone_the_picker_is_a_sheet_that_writes_its_day_when_select_date_is_pressed() => RunAsync(async () =>
    {
        await OpenAsync();
        await Page.SetViewportSizeAsync(390, 844);
        try
        {
            var picker = Page.Locator("[data-testid='ui-date-picker'] [data-ui-date-picker]").First;
            var field = picker.Locator("[data-ui-date-picker-button]");
            var state = Page.Locator("[data-testid='ui-date-picker-state']");
            var dialog = picker.Locator("dialog");
            await field.ScrollIntoViewIfNeededAsync();
            await field.ClickAsync();
            await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
            // The page has heard that this is the sheet: no day closes the popup any more.
            await Expect(dialog.Locator("[popovertargetaction]")).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
            var box = await dialog.BoundingBoxAsync();
            var day = await dialog.Locator("td[data-date]:not([data-outside]) button:not([disabled])").Nth(4).BoundingBoxAsync();

            // A pick marks the day; Cancel leaves the value as it was.
            await dialog.Locator("td[data-date]:not([data-outside]) button:not([disabled])").Nth(4).ClickAsync();
            await Expect(dialog.Locator("td[data-selected]")).ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
            await Expect(dialog).ToBeVisibleAsync();
            await Expect(state).ToHaveTextAsync("No arrival chosen.");
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel", Exact = true }).ClickAsync();
            await Expect(dialog).ToBeHiddenAsync();
            await Expect(field).ToContainTextAsync("Select a date");

            // Opened again, another day, and the button writes it.
            await field.ClickAsync();
            await Expect(dialog).ToBeVisibleAsync();
            await dialog.Locator("td[data-date]:not([data-outside]) button:not([disabled])").Nth(6).ClickAsync();
            await Expect(dialog.Locator("td[data-selected] button")).ToHaveTextAsync("7", new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
            await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Select date", Exact = true }).Last.ClickAsync();

            await Expect(dialog).ToBeHiddenAsync();
            await Expect(state).ToContainTextAsync("Arrival: ", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
            await Expect(field).Not.ToContainTextAsync("Select a date");
            Assert.Equal(324, box!.Width);
            Assert.Equal(33, box.X);
            Assert.Equal(44, day!.Width);
            Assert.Equal(44, day.Height);
        }
        finally
        {
            await Page.SetViewportSizeAsync(1280, 720);
        }
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
    public Task A_clear_button_sits_inside_its_picker_in_a_row_and_empties_it() => RunAsync(async () =>
    {
        await OpenAsync();
        var row = Page.Locator("[data-testid='ui-picker-clear']");
        var state = Page.Locator("[data-testid='ui-picker-clear-state']");
        await row.ScrollIntoViewIfNeededAsync();

        foreach (var trigger in new[] { "[data-ui-date-picker-button]", "[data-ui-time-picker-button]" })
        {
            var box = (await row.Locator(trigger).BoundingBoxAsync())!;
            var clear = row.Locator(trigger).Locator("xpath=ancestor::*[@data-ui-control][1]").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Clear", Exact = true });
            var button = (await clear.BoundingBoxAsync())!;

            // Inside the trigger, centred on it, before its chevron: not under the row.
            Assert.InRange(button.Y, box.Y, box.Y + box.Height - button.Height);
            Assert.InRange(button.X + button.Width, box.X + (box.Width / 2), box.X + box.Width - 20);
            Assert.Equal(box.Y + (box.Height / 2), button.Y + (button.Height / 2), 1.0);
            await clear.ClickAsync();
        }

        await Expect(state).ToHaveTextAsync("No day, no time", new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(row.Locator("[data-ui-date-picker-button]")).ToContainTextAsync("Select a date");
        await Expect(row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Clear", Exact = true })).ToHaveCountAsync(0);
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
        var stop = calendar.Locator("td button[tabindex='0']");
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

        // Three reads of 64 kB: each is a task of its own and the observer notes every one, so the file is seen
        // part-way on any machine. No more than three, because a handler that has outlived its repaint budget
        // repaints its component at every await, and this handler's component is the whole page: two dozen
        // reads were two dozen page renders on a slow runner and outlasted the wait (CI run 38281784445).
        await upload.Locator("input[type=file]").SetInputFilesAsync(
            new FilePayload { Name = "film.bin", MimeType = "application/octet-stream", Buffer = new byte[150_000] });
        await Expect(progress.Locator("[data-ui-file-item]")).ToContainTextAsync(
            "film.bin", new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });
        await Expect(upload).Not.ToHaveAttributeAsync(
            "data-loading", "", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
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
