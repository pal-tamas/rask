using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The calendar, date picker and time picker on their pages in the UI tab, in a browser: the keys, the focus, the
///     popup and the sheet a phone gets, which no markup assertion can see.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitPickersTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Theory]
    [InlineData("calendar", "Calendar", "ui-calendar|ui-calendar-bound|ui-calendar-multiple|ui-calendar-range|ui-calendar-range-limits|ui-calendar-size|ui-calendar-static|ui-calendar-max|ui-calendar-unavailable|ui-calendar-today|ui-calendar-header|ui-calendar-inputs|ui-calendar-fixed|ui-calendar-start|ui-calendar-open|ui-calendar-force|ui-calendar-weeks|ui-calendar-locale")]
    [InlineData("date-picker", "Date picker", "ui-date-picker|ui-date-picker-bound|ui-date-picker-input|ui-date-picker-range|ui-date-picker-limits|ui-date-picker-range-inputs|ui-date-picker-with-inputs|ui-date-picker-native|ui-date-picker-presets|ui-date-picker-preset-list|ui-date-picker-unavailable|ui-date-picker-today|ui-date-picker-header|ui-date-picker-fixed|ui-date-picker-start|ui-date-picker-open|ui-date-picker-weeks|ui-date-picker-locale|ui-date-picker-clearable")]
    [InlineData("datetime-picker", "Datetime picker", "ui-datetime-picker|ui-datetime-picker-bound|ui-datetime-picker-range|ui-datetime-picker-limits|ui-datetime-picker-input|ui-datetime-picker-interval|ui-datetime-picker-default|ui-datetime-picker-min-max|ui-datetime-picker-min-max-times|ui-datetime-picker-unavailable|ui-datetime-picker-format|ui-datetime-picker-clearable|ui-datetime-picker-confirmation|ui-datetime-picker-trigger|ui-datetime-picker-calendar|ui-datetime-picker-open|ui-datetime-picker-locale|ui-datetime-picker-separate")]
    [InlineData("time-picker", "Time picker", "ui-time-picker|ui-time-picker-bound|ui-time-picker-input|ui-time-picker-typed|ui-time-picker-multiple|ui-time-picker-format|ui-time-picker-interval|ui-time-picker-min-max|ui-time-picker-unavailable|ui-time-picker-open|ui-time-picker-locale")]
    public Task Every_example_renders_with_a_real_size(string slug, string heading, string testids) => RunAsync(async () =>
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
    public Task A_datetime_pickers_day_and_time_make_its_value_and_the_time_closes_it() => RunAsync(async () =>
    {
        await OpenUiPageAsync("datetime-picker", "Datetime picker");

        var picker = Page.Locator("[data-testid='ui-datetime-picker-confirmation'] [data-ui-datetime-picker]").First;
        var range = Page.Locator("[data-testid='ui-datetime-picker-range'] [data-ui-datetime-picker]").First;
        var state = Page.Locator("[data-testid='ui-datetime-picker-range-state']");
        await range.ScrollIntoViewIfNeededAsync();
        await range.Locator("[data-ui-datetime-picker-button]").ClickAsync();
        var days = range.Locator("dialog td[data-date]:not([data-outside]) button:not([disabled])");
        await days.Nth(3).ClickAsync();
        await days.Nth(5).ClickAsync();
        await range.Locator("[data-ui-datetime-picker-time=start] button[role=combobox]").ClickAsync();
        await range.Locator("[data-ui-datetime-picker-time=start] [data-time='09:00']").ClickAsync();
        await range.Locator("[data-ui-datetime-picker-time=end] button[role=combobox]").ClickAsync();
        await range.Locator("[data-ui-datetime-picker-time=end] [data-time='17:00']").ClickAsync();
        await range.Locator("[data-ui-datetime-picker-select]").ClickAsync();

        await Expect(state).ToContainTextAsync("T09:00 to ", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(state).ToContainTextAsync("T17:00");
        await Expect(picker.Locator("[data-ui-datetime-picker-select]")).ToBeDisabledAsync();
    });

    [Fact]
    public Task A_datetime_pickers_time_picked_beside_a_day_closes_it_with_the_moment_on_the_button() => RunAsync(async () =>
    {
        await OpenUiPageAsync("datetime-picker", "Datetime picker");

        var picker = Page.Locator("[data-testid='ui-datetime-picker'] [data-ui-datetime-picker]").First;
        var state = Page.Locator("[data-testid='ui-datetime-picker-state']");
        await picker.ScrollIntoViewIfNeededAsync();
        await picker.Locator("[data-ui-datetime-picker-button]").ClickAsync();
        await picker.Locator("dialog td[data-date]:not([data-outside]) button:not([disabled])").Nth(10).ClickAsync();
        await picker.Locator("[data-rask-calendar-part=times] [data-time='14:30']").ClickAsync();

        await Expect(state).ToContainTextAsync("T14:30", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(picker.Locator("dialog")).Not.ToBeVisibleAsync();
        await Expect(picker.Locator("[data-ui-datetime-picker-button]")).ToContainTextAsync("at 2:30 PM");
    });

    [Fact]
    public Task The_old_data_input_page_sends_the_reader_on_to_the_date_picker_page() => RunAsync(async () =>
    {
        await Page.GotoAsync($"{Docs}/ui/data-input/");

        await Expect(Page).ToHaveURLAsync(new Regex("/ui/date-picker/$"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });

        await Expect(Page.Locator("main h1")).ToContainTextAsync("Date picker",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    });

    [Fact]
    public Task The_calendar_changes_month_and_picks_a_day() => RunAsync(async () =>
    {
        await OpenUiPageAsync("calendar", "Calendar");

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
    public Task A_range_is_written_only_once_it_has_both_ends() => RunAsync(async () =>
    {
        await OpenUiPageAsync("calendar", "Calendar");

        var calendar = Page.Locator("[data-testid='ui-calendar-range'] [data-ui-calendar]");
        await calendar.ScrollIntoViewIfNeededAsync();
        var days = calendar.Locator("[data-month]").First.Locator("td[data-date]:not([data-outside])");
        var state = Page.Locator("[data-testid='ui-calendar-stay']");

        // The first click is drawn as the start and written nowhere: the page still has no stay.
        await days.Nth(9).Locator("button").ClickAsync();
        await Expect(days.Nth(9)).ToHaveAttributeAsync("data-start", "",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await Expect(state).ToHaveTextAsync("No range chosen.");

        await days.Nth(14).Locator("button").ClickAsync();
        await Expect(state).ToContainTextAsync("-10 to ", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(state).ToContainTextAsync("-15");
        await Expect(days.Nth(12)).ToHaveAttributeAsync("data-in-range", "");
    });

    [Fact]
    public Task The_arrows_carry_the_focus_through_the_calendar_and_the_page_does_not_scroll() => RunAsync(async () =>
    {
        await OpenUiPageAsync("calendar", "Calendar");

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
    public Task An_open_picker_holds_the_focus_with_no_ring_after_a_key() => RunAsync(async () =>
    {
        await OpenUiPageAsync("date-picker", "Date picker");
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
        await OpenUiPageAsync("date-picker", "Date picker");

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
        await Expect(Page.Locator("[data-testid='ui-date-picker-state']")).ToContainTextAsync("Chosen: ",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(field).Not.ToContainTextAsync("Select a date");
    });

    [Fact]
    public Task On_a_phone_the_picker_is_a_sheet_that_writes_its_day_when_select_date_is_pressed() => RunAsync(async () =>
    {
        await OpenUiPageAsync("date-picker", "Date picker");
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
            await Expect(state).ToHaveTextAsync("No date chosen.");
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
            await Expect(state).ToContainTextAsync("Chosen: ", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
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
        await OpenUiPageAsync("date-picker", "Date picker");

        var picker = Page.Locator("[data-testid='ui-date-picker-presets'] [data-ui-date-picker]");
        var field = picker.Locator("[data-ui-date-picker-button]");
        await field.ScrollIntoViewIfNeededAsync();
        await field.ClickAsync();

        var dialog = picker.Locator("dialog");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await dialog.GetByRole(AriaRole.Radio, new LocatorGetByRoleOptions { Name = "Today", Exact = true }).ClickAsync();

        await Expect(dialog).ToBeHiddenAsync();
        await Expect(field).ToContainTextAsync("Today", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("[data-testid='ui-date-picker-presets-state']")).ToContainTextAsync("Range: ");
    });

    [Fact]
    public Task An_arrow_in_the_presets_chooses_the_next_one_which_closes_the_picker() => RunAsync(async () =>
    {
        await OpenUiPageAsync("date-picker", "Date picker");

        var picker = Page.Locator("[data-testid='ui-date-picker-presets'] [data-ui-date-picker]");
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
    public Task A_clear_button_sits_inside_its_picker_in_a_row_and_empties_it() => RunAsync(async () =>
    {
        await OpenUiPageAsync("date-picker", "Date picker");
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
    public Task A_date_typed_into_the_fields_is_committed_and_only_the_room_beside_them_opens_the_calendar() => RunAsync(async () =>
    {
        await OpenUiPageAsync("date-picker", "Date picker");

        var picker = Page.Locator("[data-testid='ui-date-picker-input'] [data-ui-date-picker]");
        var dialog = picker.Locator("dialog");
        await picker.ScrollIntoViewIfNeededAsync();

        // A press in a field puts the caret there and opens nothing. 3 can only be March, so it moves on.
        await picker.Locator("[data-rask-segment='month']").ClickAsync();
        await Page.Keyboard.TypeAsync("3152027", new KeyboardTypeOptions { Delay = 60 });

        await Expect(Page.Locator("[data-testid='ui-date-picker-input-state']")).ToHaveTextAsync("Chosen: 2027-03-15",
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
    public Task A_time_picker_opens_its_list_and_closes_on_the_pick() => RunAsync(async () =>
    {
        await OpenUiPageAsync("time-picker", "Time picker");

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
    public Task The_time_list_follows_the_keyboard_inside_itself_and_a_typed_time_is_committed() => RunAsync(async () =>
    {
        await OpenUiPageAsync("time-picker", "Time picker");

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
        var typed = Page.Locator("[data-testid='ui-time-picker-input'] [data-ui-time-picker]");
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
}
