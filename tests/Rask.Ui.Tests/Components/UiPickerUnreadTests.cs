using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Rask.Testing;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

/// <summary>
///     A typed date or time picker left with parts that make no value: half filled it says "enter a valid
///     date" under the field and the save is refused, where the stored value used to be saved in its place;
///     with every part emptied it is nothing chosen.
/// </summary>
/// <remarks>
///     The parts are the browser's (<c>rask-segments.ts</c>): what reaches the page is the one hidden field's
///     <c>change</c>, with an empty value, and <c>bad</c> beside it when the parts were left half filled.
/// </remarks>
public partial class UiPickerUnreadTests : global::Rask.Core.RaskMarkup
{
    private const string Whole = "input[type=\"hidden\"]";
    private const string HalfDate = """{"type":"change","value":"","bad":"date"}""";
    private const string HalfTime = """{"type":"change","value":"","bad":"time"}""";

    private static readonly DateOnly Today = new(2026, 1, 15);

    private static Page Booking(Trip trip, Action? saved = null) => Acting.Render(() =>
        Form.Model(trip).OnSubmit(_ => saved?.Invoke())[
            Ui.DatePicker.Bind(() => trip.Leaves).Type(Ui.DatePickerType.Input).Locale("en-US").On(Today).Label("Leaves").Id("leaves"),
            Ui.TimePicker.Bind(() => trip.At).Type(Ui.TimePickerType.Input).Locale("en-US").Label("At").Id("at")
        ]);

    [Fact]
    public async Task A_date_left_half_typed_keeps_the_stored_day_out_of_the_save_and_says_so_under_the_field()
    {
        var trip = new Trip { Leaves = new DateOnly(2026, 3, 15) };
        var saves = 0;
        var page = Booking(trip, () => saves++);

        await page.On("#leaves " + Whole).Raise("change", HalfDate);
        var whileItWaits = page.TextOf("#leaves-error");
        await page.On("form").Submit();

        Assert.Equal("", whileItWaits);
        Assert.Equal("Enter a valid date.", page.TextOf("#leaves-error"));
        Assert.Equal("true", page.Find("#leaves").Attribute("aria-invalid"));
        Assert.Equal(0, saves);
        Assert.Equal(new DateOnly(2026, 3, 15), trip.Leaves);
    }

    [Fact]
    public async Task A_time_left_half_typed_says_so_in_hungarian_out_of_the_kits_catalog()
    {
        var trip = new Trip { Leaves = Today, At = new TimeOnly(9, 30) };
        var saves = 0;
        var page = Booking(trip, () => saves++);
        var language = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hu-HU");

        await page.On("[data-ui-time-picker] " + Whole).Raise("change", HalfTime);
        await page.On("form").Submit();
        CultureInfo.CurrentUICulture = language;

        Assert.Equal("Érvényes időpontot adjon meg.", page.TextOf("#at-error"));
        Assert.Equal(0, saves);
        Assert.Equal(new TimeOnly(9, 30), trip.At);
    }

    [Fact]
    public async Task A_picker_with_every_part_emptied_is_nothing_chosen_and_a_required_one_says_required()
    {
        var trip = new Trip { Leaves = new DateOnly(2026, 3, 15), At = new TimeOnly(9, 30) };
        var saves = 0;
        var page = Booking(trip, () => saves++);

        await page.On("#leaves " + Whole).Change(string.Empty);
        await page.On("[data-ui-time-picker] " + Whole).Change(string.Empty);
        await page.On("form").Submit();

        Assert.Null(trip.Leaves);
        Assert.Null(trip.At);
        Assert.Equal("The Leaves field is required.", page.TextOf("#leaves-error"));
        Assert.Equal("", page.TextOf("#at-error"));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_whole_date_typed_after_a_half_one_takes_the_sentence_away_and_saves()
    {
        var trip = new Trip { Leaves = new DateOnly(2026, 3, 15) };
        var saves = 0;
        var page = Booking(trip, () => saves++);

        await page.On("#leaves " + Whole).Raise("change", HalfDate);
        await page.On("form").Submit();
        await page.On("#leaves " + Whole).Change("2026-04-02");
        await page.On("form").Submit();

        Assert.Equal("", page.TextOf("#leaves-error"));
        Assert.Equal(new DateOnly(2026, 4, 2), trip.Leaves);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task The_first_key_of_a_correction_takes_the_sentence_away_and_the_parts_stay_no_date()
    {
        var trip = new Trip { Leaves = new DateOnly(2026, 3, 15) };
        var saves = 0;
        var page = Booking(trip, () => saves++);
        var whileValid = !page.Exists("[data-rask-on-edit]");
        await page.On("#leaves " + Whole).Raise("change", HalfDate);
        await page.On("form").Submit();

        await page.Invoke(page.Find("#leaves [data-rask-segments]").Attribute("data-rask-on-edit")!, """{"type":"edit"}""");
        var afterTheFirstKey = page.TextOf("#leaves-error");
        await page.On("form").Submit();

        Assert.True(whileValid);
        Assert.Equal("", afterTheFirstKey);
        Assert.Equal("Enter a valid date.", page.TextOf("#leaves-error"));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_live_picker_says_it_as_the_parts_are_left()
    {
        var trip = new Trip { Leaves = new DateOnly(2026, 3, 15) };
        var page = Page.Render(() => Form.Model(trip)[
            Ui.DatePicker.Bind(() => trip.Leaves).Live().Type(Ui.DatePickerType.Input).Locale("en-US").On(Today).Label("Leaves").Id("leaves")]);

        await page.On("#leaves " + Whole).Raise("change", HalfDate);

        Assert.Equal("Enter a valid date.", page.TextOf("#leaves-error"));
        Assert.Equal(new DateOnly(2026, 3, 15), trip.Leaves);
    }

    [Fact]
    public async Task The_clear_button_ends_where_erasing_every_part_does_and_a_required_picker_says_required_at_the_save()
    {
        var trip = new Trip { Leaves = new DateOnly(2026, 3, 15), At = new TimeOnly(9, 30) };
        var saves = 0;
        var page = Acting.Render(() => Form.Model(trip).OnSubmit(_ => saves++)[
            Ui.DatePicker.Bind(() => trip.Leaves).Clearable().Type(Ui.DatePickerType.Input).Locale("en-US").On(Today).Label("Leaves").Id("leaves"),
            Ui.TimePicker.Bind(() => trip.At).Clearable().Type(Ui.TimePickerType.Input).Locale("en-US").Label("At").Id("at")
        ]);

        await page.On("#leaves " + Whole).Raise("change", HalfDate);
        await page.On("[data-ui-date-picker] [aria-label=\"Clear\"]").Click();
        await page.On("[data-ui-time-picker] [aria-label=\"Clear\"]").Click();
        await page.On("form").Submit();

        Assert.Null(trip.Leaves);
        Assert.Null(trip.At);
        Assert.Equal("The Leaves field is required.", page.TextOf("#leaves-error"));
        Assert.Equal("", page.TextOf("#at-error"));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task The_clear_button_shows_an_empty_picker_at_once_and_writes_the_model_with_the_next_action()
    {
        var trip = new Trip { Leaves = new DateOnly(2026, 3, 15) };
        var page = Acting.Render(() => Form.Model(trip)[
            Ui.DatePicker.Bind(() => trip.Leaves).Clearable().Locale("en-US").On(Today).Label("Leaves").Id("leaves")]);

        await page.On("[aria-label=\"Clear\"]").Click();
        var shown = page.Html;
        var held = trip.Leaves;
        await Acting.Act(page);

        Assert.Contains(">Select a date</span>", shown, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 3, 15), held);
        Assert.Null(trip.Leaves);
    }

    [Fact]
    public async Task The_clear_button_of_a_live_picker_writes_the_model_at_once()
    {
        var trip = new Trip { Leaves = new DateOnly(2026, 3, 15) };
        var page = Page.Render(() => Form.Model(trip)[
            Ui.DatePicker.Bind(() => trip.Leaves).Live().Clearable().Locale("en-US").On(Today).Label("Leaves").Id("leaves")]);

        await page.On("[aria-label=\"Clear\"]").Click();

        Assert.Null(trip.Leaves);
        Assert.Contains(">Select a date</span>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_range_with_one_end_emptied_is_no_range_and_with_both_emptied_is_nothing_chosen()
    {
        var trip = new Trip { Leaves = Today, Stay = new UiDateRange(new DateOnly(2026, 1, 4), new DateOnly(2026, 1, 6)) };
        var page = Acting.Render(() => Form.Model(trip)[
            Ui.DatePicker.Range.Bind(() => trip.Stay).Locale("en-US").On(Today).Label("Stay").Id("stay")
                .Trigger(Div[Ui.DatePickerInput.Label("Start"), Ui.DatePickerInput.Label("End")])]);

        await page.On("#f-start " + Whole).Change(string.Empty);
        await Acting.Act(page);
        var oneEnd = (trip.Stay, Said: page.Html.Contains("Enter a valid date.", StringComparison.Ordinal));
        await page.On("#f-end " + Whole).Change(string.Empty);
        await Acting.Act(page);

        Assert.Equal(new UiDateRange(new DateOnly(2026, 1, 4), new DateOnly(2026, 1, 6)), oneEnd.Stay);
        Assert.True(oneEnd.Said);
        Assert.Equal(default, trip.Stay);
        Assert.DoesNotContain("Enter a valid date.", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_time_that_cannot_be_unset_says_so_when_its_parts_are_emptied()
    {
        var shift = new Shift();
        var page = Acting.Render(() => Form.Model(shift)[
            Ui.TimePicker.Bind(() => shift.Starts).Type(Ui.TimePickerType.Input).Locale("en-US").Label("Starts").Id("starts")]);

        await page.On(Whole).Change(string.Empty);
        await Acting.Act(page);

        Assert.Equal("Enter a valid time.", page.TextOf("#starts-error"));
        Assert.Equal(new TimeOnly(8, 0), shift.Starts);
    }

    [Fact]
    public async Task A_controlled_picker_hears_nothing_of_parts_left_half_filled()
    {
        var heard = new List<DateOnly>();
        var page = Page.Render(() => Ui.DatePicker.Value(Today).OnChange(heard.Add).Type(Ui.DatePickerType.Input).Locale("en-US").On(Today));

        await page.On(Whole).Raise("change", HalfDate);

        Assert.Empty(heard);
    }

    private sealed class Trip
    {
        [Required]
        public DateOnly? Leaves { get; set; }

        public TimeOnly? At { get; set; }

        public UiDateRange Stay { get; set; }
    }

    private sealed class Shift
    {
        public TimeOnly Starts { get; set; } = new(8, 0);
    }
}
