using System.Globalization;
using Rask.Core.Globalization;
using Rask.Testing;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

/// <summary>
///     A date, a range or a time the reader typed outside its picker's limits (#1273) — before <c>Min</c>, after
///     <c>Max</c>, <c>Unavailable</c>, shorter than <c>MinRange</c> or longer than <c>MaxRange</c>, a datetime's time
///     outside its time limits — is not taken: the model keeps what it held, the field says why under it, and the save
///     is refused until a value within them arrives, as for a date half typed.
/// </summary>
public partial class UiPickerLimitsTests : global::Rask.Core.RaskMarkup
{
    private const string Whole = "[data-rask-segments] input[type=\"hidden\"]";

    private static readonly DateOnly Today = new(2026, 1, 15);
    private static readonly DateOnly Kept = new(2026, 3, 10);

    private static Page Booking(Trip trip, Action saved) => Acting.Render(() =>
        Form.Model(trip).OnSubmit(_ => saved())[
            Ui.DatePicker.Bind(() => trip.Leaves).Type(Ui.DatePickerType.Input).Locale("en-US").On(Today).Label("Leaves").Id("leaves")
                .Min(new DateOnly(2026, 3, 1)).Max(new DateOnly(2026, 3, 31)).Unavailable([new DateOnly(2026, 3, 13)])
        ]);

    [Fact]
    public async Task A_date_typed_before_the_min_is_not_taken_and_the_field_says_so()
    {
        var trip = new Trip { Leaves = Kept };
        var saves = 0;
        var page = Booking(trip, () => saves++);

        await page.On("#leaves " + Whole).Change("2026-02-27");
        await page.On("form").Submit();

        Assert.Equal(Kept, trip.Leaves);
        Assert.Equal("Choose a date on or after Mar 1, 2026.", page.TextOf("#leaves-error"));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_date_typed_after_the_max_is_not_taken_and_the_field_says_so()
    {
        var trip = new Trip { Leaves = Kept };
        var saves = 0;
        var page = Booking(trip, () => saves++);

        await page.On("#leaves " + Whole).Change("2026-04-02");
        await page.On("form").Submit();

        Assert.Equal(Kept, trip.Leaves);
        Assert.Equal("Choose a date on or before Mar 31, 2026.", page.TextOf("#leaves-error"));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task An_unavailable_date_typed_is_not_taken_and_the_field_says_so()
    {
        var trip = new Trip { Leaves = Kept };
        var saves = 0;
        var page = Booking(trip, () => saves++);

        await page.On("#leaves " + Whole).Change("2026-03-13");
        await page.On("form").Submit();

        Assert.Equal(Kept, trip.Leaves);
        Assert.Equal("Mar 13, 2026 is not available.", page.TextOf("#leaves-error"));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_date_typed_within_the_limits_is_taken_after_one_that_was_not()
    {
        var trip = new Trip { Leaves = Kept };
        var saves = 0;
        var page = Booking(trip, () => saves++);
        await page.On("#leaves " + Whole).Change("2026-04-02");
        await page.On("form").Submit();

        await page.On("#leaves " + Whole).Change("2026-03-20");
        await page.On("form").Submit();

        Assert.Equal(new DateOnly(2026, 3, 20), trip.Leaves);
        Assert.Equal("", page.TextOf("#leaves-error"));
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task A_controlled_picker_hears_nothing_of_a_date_typed_outside_its_limits()
    {
        var heard = new List<DateOnly>();
        var page = Page.Render(() => Ui.DatePicker.Value(Kept).OnChange(heard.Add).Type(Ui.DatePickerType.Input)
            .Max(new DateOnly(2026, 3, 31)).Locale("en-US").On(Today));

        await page.On(Whole).Change("2026-04-02");

        Assert.Empty(heard);
    }

    [Theory]
    [InlineData("2026-03-03", "Choose a range of at least 3 days.")]
    [InlineData("2026-02-20", "Choose a range of at most 7 days.")]
    public async Task A_range_start_typed_outside_its_length_limits_is_not_taken_and_the_field_says_so(string start, string said)
    {
        var trip = new Trip { Stay = new UiDateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 4)) };
        var page = Page.Render(() => Form.Model(trip)[
            Ui.DatePicker.Range.Bind(() => trip.Stay).Live().Type(Ui.DatePickerType.Input).MinRange(3).MaxRange(7)
                .Locale("en-US").On(Today).Label("Stay").Id("stay")]);

        await page.On("#stay " + Whole).Change(start);

        Assert.Equal(new UiDateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 4)), trip.Stay);
        Assert.Equal(said, page.TextOf("#stay-error"));
    }

    [Fact]
    public async Task A_range_the_calendar_announced_longer_than_its_max_range_is_not_taken_and_the_field_says_so()
    {
        var trip = new Trip { Stay = new UiDateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 4)) };
        var page = Page.Render(() => Form.Model(trip)[
            Ui.DatePicker.Range.Bind(() => trip.Stay).Live().MaxRange(7).Locale("en-US").On(Today).Label("Stay").Id("stay")]);

        await page.Invoke(page.HandlerIds("input")[0], "{\"type\":\"input\",\"value\":\"2026-03-01/2026-03-20\"}");

        Assert.Equal(new UiDateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 4)), trip.Stay);
        Assert.Equal("Choose a range of at most 7 days.", page.TextOf("#stay-error"));
    }

    [Fact]
    public async Task A_range_within_its_length_limits_is_taken()
    {
        var trip = new Trip { Stay = new UiDateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 4)) };
        var page = Page.Render(() => Form.Model(trip)[
            Ui.DatePicker.Range.Bind(() => trip.Stay).Live().MinRange(3).MaxRange(7).Locale("en-US").On(Today).Label("Stay").Id("stay")]);

        await page.Invoke(page.HandlerIds("input")[0], "{\"type\":\"input\",\"value\":\"2026-03-01/2026-03-06\"}");

        Assert.Equal(new UiDateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 6)), trip.Stay);
        Assert.Equal("", page.TextOf("#stay-error"));
    }

    [Fact]
    public async Task A_range_a_calendar_announced_longer_than_its_max_range_is_not_taken()
    {
        var trip = new Trip { Stay = new UiDateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 4)) };
        var page = Page.Render(() => Form.Model(trip)[Ui.Calendar.Range.Bind(() => trip.Stay).Live().MaxRange(7).Locale("en-US").On(Today)]);

        await page.Invoke(page.HandlerIds("input")[0], "{\"type\":\"input\",\"value\":\"2026-03-01/2026-03-20\"}");

        Assert.Equal(new UiDateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 4)), trip.Stay);
    }

    [Theory]
    [InlineData("07:30", "Choose a time at or after 9:00 AM.")]
    [InlineData("18:00", "Choose a time at or before 5:00 PM.")]
    [InlineData("12:15", "12:15 PM is not available.")]
    public async Task A_time_typed_outside_its_limits_is_not_taken_and_the_field_says_so(string typed, string said)
    {
        var trip = new Trip { At = new TimeOnly(10, 0) };
        var page = Page.Render(() => Form.Model(trip)[
            Ui.TimePicker.Bind(() => trip.At).Live().Type(Ui.TimePickerType.Input).Locale("en-US").Label("At").Id("at")
                .Min(new TimeOnly(9, 0)).Max(new TimeOnly(17, 0)).Unavailable([new UiTimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0))])]);

        await page.On("[data-ui-time-picker] " + Whole).Change(typed);

        Assert.Equal(new TimeOnly(10, 0), trip.At);
        Assert.Equal(said, page.TextOf("#at-error"));
    }

    [Fact]
    public async Task A_time_typed_within_its_limits_is_taken()
    {
        var trip = new Trip { At = new TimeOnly(10, 0) };
        var page = Page.Render(() => Form.Model(trip)[
            Ui.TimePicker.Bind(() => trip.At).Live().Type(Ui.TimePickerType.Input).Locale("en-US").Label("At").Id("at")
                .Min(new TimeOnly(9, 0)).Max(new TimeOnly(17, 0))]);

        await page.On("[data-ui-time-picker] " + Whole).Change("16:45");

        Assert.Equal(new TimeOnly(16, 45), trip.At);
        Assert.Equal("", page.TextOf("#at-error"));
    }

    [Theory]
    [InlineData("2026-03-02T07:00", "Choose a time at or after 9:00 AM.")]
    [InlineData("2026-03-31T16:00", "Choose a time at or before 2:00 PM.")]
    public async Task A_datetime_whose_time_is_outside_its_limits_is_not_taken_and_the_field_says_so(string sent, string said)
    {
        var trip = new Trip { Departs = new DateTime(2026, 3, 10, 10, 0, 0) };
        var page = Page.Render(() => Form.Model(trip)[
            Ui.DatetimePicker.Bind(() => trip.Departs).Live().Locale("en-US").On(Today).Label("Departs").Id("departs")
                .MinTime(new TimeOnly(9, 0)).Max(new DateTime(2026, 3, 31, 14, 0, 0))]);

        await page.Invoke(page.HandlerIds("input")[0], "{\"type\":\"input\",\"value\":\"" + sent + "\"}");

        Assert.Equal(new DateTime(2026, 3, 10, 10, 0, 0), trip.Departs);
        Assert.Equal(said, page.TextOf("#departs-error"));
    }

    [Fact]
    public async Task A_datetime_within_its_limits_is_taken()
    {
        var trip = new Trip { Departs = new DateTime(2026, 3, 10, 10, 0, 0) };
        var page = Page.Render(() => Form.Model(trip)[
            Ui.DatetimePicker.Bind(() => trip.Departs).Live().Locale("en-US").On(Today).Label("Departs").Id("departs")
                .MinTime(new TimeOnly(9, 0)).Max(new DateTime(2026, 3, 31, 14, 0, 0))]);

        await page.Invoke(page.HandlerIds("input")[0], "{\"type\":\"input\",\"value\":\"2026-03-31T13:30\"}");

        Assert.Equal(new DateTime(2026, 3, 31, 13, 30, 0), trip.Departs);
        Assert.Equal("", page.TextOf("#departs-error"));
    }

    [Theory]
    [InlineData(RaskString.ValidationDateMin, "Choose a date on or after X.", "Válasszon X vagy későbbi dátumot.")]
    [InlineData(RaskString.ValidationDateMax, "Choose a date on or before X.", "Válasszon X vagy korábbi dátumot.")]
    [InlineData(RaskString.ValidationUnavailable, "X is not available.", "X nem választható.")]
    [InlineData(RaskString.ValidationRangeMin, "Choose a range of at least X days.", "Legalább X napos időszakot válasszon.")]
    [InlineData(RaskString.ValidationRangeMax, "Choose a range of at most X days.", "Legfeljebb X napos időszakot válasszon.")]
    [InlineData(RaskString.ValidationTimeMin, "Choose a time at or after X.", "Válasszon X vagy későbbi időpontot.")]
    [InlineData(RaskString.ValidationTimeMax, "Choose a time at or before X.", "Válasszon X vagy korábbi időpontot.")]
    public void A_limit_sentence_is_said_in_english_and_in_hungarian(RaskString sentence, string english, string hungarian)
    {
        var language = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var inEnglish = RaskStrings.Get(sentence, english.Replace("X", "{0}", StringComparison.Ordinal), "X");

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hu-HU");
        var inHungarian = RaskStrings.Get(sentence, english.Replace("X", "{0}", StringComparison.Ordinal), "X");
        CultureInfo.CurrentUICulture = language;

        Assert.Equal(english, inEnglish);
        Assert.Equal(hungarian, inHungarian);
    }

    private sealed class Trip
    {
        public DateOnly? Leaves { get; set; }

        public TimeOnly? At { get; set; }

        public UiDateRange Stay { get; set; }

        public DateTime Departs { get; set; }
    }
}
