using System.ComponentModel.DataAnnotations;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The kit's typed date and time pickers on an edit form, in a real browser: parts left half filled are not
///     saved as the value the row already had. The field says so, the first key of a correction takes the
///     sentence away, and a picker with every part emptied is nothing chosen.
/// </summary>
public sealed class PickerUnreadTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Stored = "saved=";

    private static ILocator Part(IPage page, string field, string kind, int nth = 0) =>
        page.Locator($"{field} [data-rask-segment=\"{kind}\"]").Nth(nth);

    private static async Task EraseAsync(IPage page, string field, string kind, int nth = 0)
    {
        await Part(page, field, kind, nth).ClickAsync();
        await page.Keyboard.PressAsync("Backspace");
    }

    [Fact]
    public async Task A_date_with_its_day_erased_is_told_in_hungarian_and_the_stored_day_is_not_saved_in_its_place()
    {
        await using var session = await HookSession.OpenAsync<PickerEditPage>(
            playwright, new() { Locale = "hu-HU", ViewportSize = new() { Width = 1000, Height = 700 } },
            cultures: o => o.SupportedCultures.Add("hu"));
        var page = session.Page;

        await EraseAsync(page, "#leaves", "day");
        await page.ClickAsync("#save");
        await Expect(page.Locator("#leaves-error")).ToHaveTextAsync("Érvényes dátumot adjon meg.");
        var savedWhileHalf = await page.Locator("#saved").TextContentAsync();
        await Part(page, "#leaves", "day").ClickAsync();
        await page.Keyboard.PressAsync("2");
        await Expect(page.Locator("#leaves-error")).ToBeHiddenAsync();
        await Expect(Part(page, "#leaves", "month")).ToHaveValueAsync("03");
        await page.Keyboard.PressAsync("0");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToContainTextAsync("saved=2026-03-20|");
        Assert.Equal(Stored, savedWhileHalf);
    }

    [Fact]
    public async Task A_time_with_its_minute_erased_says_so_and_a_typed_minute_saves()
    {
        await using var session = await HookSession.OpenAsync<PickerEditPage>(playwright);
        var page = session.Page;

        await EraseAsync(page, "[data-ui-time-picker]", "minute");
        await page.ClickAsync("#save");
        await Expect(page.Locator("#at-error")).ToHaveTextAsync("Enter a valid time.");
        var savedWhileHalf = await page.Locator("#saved").TextContentAsync();
        await Part(page, "[data-ui-time-picker]", "minute").ClickAsync();
        await page.Keyboard.TypeAsync("45");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToContainTextAsync("|09:45|");
        await Expect(page.Locator("#at-error")).ToBeHiddenAsync();
        Assert.Equal(Stored, savedWhileHalf);
    }

    [Fact]
    public async Task A_range_with_a_part_of_its_end_erased_says_so_and_saves_nothing()
    {
        await using var session = await HookSession.OpenAsync<PickerEditPage>(playwright);
        var page = session.Page;

        await EraseAsync(page, "#f-end", "month");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#stay-error")).ToHaveTextAsync("Enter a valid date.");
        await Expect(page.Locator("#saved")).ToHaveTextAsync(Stored);
    }

    [Fact]
    public async Task A_date_with_every_part_erased_is_nothing_chosen_and_a_required_one_says_required()
    {
        await using var session = await HookSession.OpenAsync<PickerEditPage>(playwright);
        var page = session.Page;

        await EraseAsync(page, "#leaves", "month");
        await EraseAsync(page, "#leaves", "day");
        await EraseAsync(page, "#leaves", "year");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#leaves-error")).ToHaveTextAsync("The Leaves field is required.");
        await Expect(page.Locator("#saved")).ToHaveTextAsync(Stored);
    }

    [Fact]
    public async Task A_picker_nobody_touched_saves_what_it_held()
    {
        await using var session = await HookSession.OpenAsync<PickerEditPage>(playwright);
        var page = session.Page;

        await Part(page, "#leaves", "day").ClickAsync();
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=2026-03-15|09:30|2026-01-04..2026-01-06");
    }
}

/// <summary>An edit form over a stored trip: a typed date, a typed time and a range typed into two fields.</summary>
public sealed partial class PickerEditPage : Component
{
    private readonly Trip _trip = new();
    private string _saved = "";

    protected override Component? HeadAssets => Markup.Title["picker edit"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("saved")[$"saved={_saved}"],
        Form.Model(_trip).OnSubmit(Save)[
            Ui.DatePicker.Bind(() => _trip.Leaves).Type(Ui.DatePickerType.Input).Locale("en-US").Label("Leaves").Id("leaves"),
            Ui.TimePicker.Bind(() => _trip.At).Type(Ui.TimePickerType.Input).Locale("en-US").TwentyFourHour.Label("At").Id("at"),
            Ui.DatePicker.Range.Bind(() => _trip.Stay).Locale("en-US").Label("Stay").Id("stay")
                .Trigger(Div[Ui.DatePickerInput.Label("Start"), Ui.DatePickerInput.Label("End")]),
            Ui.Button.Submit.Id("save")["Save"]
        ]
    ];

    private void Save(Trip trip) => _saved = FormattableString.Invariant(
        $"{trip.Leaves:yyyy-MM-dd}|{trip.At:HH:mm}|{trip.Stay.Start:yyyy-MM-dd}..{trip.Stay.End:yyyy-MM-dd}");

    private sealed class Trip
    {
        [Required]
        public DateOnly? Leaves { get; set; } = new(2026, 3, 15);

        public TimeOnly? At { get; set; } = new(9, 30);

        public UiDateRange Stay { get; set; } = new(new DateOnly(2026, 1, 4), new DateOnly(2026, 1, 6));
    }
}
