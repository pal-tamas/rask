using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Rask.Testing;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.DayAndTime</c>: one moment, asked as a typed day and a typed time in one row (#1297), and
///     <c>Ui.DatePicker.Typed()</c>, the day typed in the culture's own order.
/// </summary>
/// <remarks>
///     The parts are the browser's (<c>rask-segments.ts</c>): what reaches the page is each picker's one hidden
///     field, "yyyy-mm-dd" for the day and "HH:mm" for the time, empty when the parts were emptied, with <c>bad</c>
///     beside it when they were left half filled.
/// </remarks>
public sealed partial class UiDayAndTimeTests : global::Rask.Core.RaskMarkup
{
    private const string Day = "#at [data-rask-segments] input[type=\"hidden\"]";
    private const string Time = "[data-ui-time-picker] [data-rask-segments] input[type=\"hidden\"]";
    private const string HalfDate = """{"type":"change","value":"","bad":"date"}""";
    private const string HalfTime = """{"type":"change","value":"","bad":"time"}""";

    private static readonly DateOnly Today = new(2026, 11, 6);

    private static Page Shift(Visit visit, Action? saved = null) => Acting.Render(() =>
        Form.Model(visit).OnSubmit(_ => saved?.Invoke())[
            Ui.DayAndTime.Bind(() => visit.At).Locale("en-US").Label("At").Id("at")
        ]);

    [Fact]
    public void The_day_and_the_time_stand_in_one_row_that_puts_the_time_beneath_where_there_is_no_room()
    {
        var visit = new Visit { At = new DateTime(2026, 11, 6, 14, 35, 0) };

        var page = Shift(visit);

        var row = page.Find("[data-ui-day-and-time]");
        Assert.True(row.HasClass("[:where(&)]:flex") && row.HasClass("[:where(&)]:flex-wrap") && row.HasClass("[:where(&)]:gap-2"));
        var day = page.Find("[data-ui-day-and-time] > [data-ui-date-picker]");
        Assert.True(day.HasClass("min-w-48") && day.HasClass("flex-1"));
        var time = page.Find("[data-ui-day-and-time] > [data-ui-time-picker]");
        Assert.True(time.HasClass("w-44") && time.HasClass("shrink-0"));
        Assert.Equal("2026-11-06", page.Find(Day).Attribute("value"));
        Assert.Equal("14:35", page.Find(Time).Attribute("value"));
    }

    [Fact]
    public void A_hungarian_day_is_typed_year_month_day_with_the_full_stops_of_2026_11_06()
    {
        var visit = new Visit();

        var page = Page.Render(() => Form.Model(visit)[Ui.DayAndTime.Bind(() => visit.At).Locale("hu-HU").Id("at")]);

        Assert.True(page.Find("[data-ui-day-and-time] > [data-ui-time-picker]").HasClass("w-36"));
        var parts = page.FindAll("#at [data-rask-segments] > *").Where(node => node.Attribute("type") != "hidden").ToList();
        Assert.Equal(
            ["year", ".\u00A0", "month", ".\u00A0", "day", "."],
            parts.Select(node => node.Attribute("data-rask-segment") ?? node.Text));
    }

    [Fact]
    public async Task A_day_typed_keeps_the_time_the_moment_had_to_the_second()
    {
        var visit = new Visit { At = new DateTime(2026, 11, 6, 14, 35, 42) };
        var page = Shift(visit);

        await page.On(Day).Change("2026-11-09");
        await Acting.Act(page);

        Assert.Equal(new DateTime(2026, 11, 9, 14, 35, 42), visit.At);
    }

    [Fact]
    public async Task A_time_left_at_the_hour_and_minute_the_moment_shows_keeps_its_seconds()
    {
        var visit = new Visit { At = new DateTime(2026, 11, 6, 14, 35, 42) };
        var page = Shift(visit);

        await page.On(Time).Change("14:35");
        await Acting.Act(page);

        Assert.Equal(new DateTime(2026, 11, 6, 14, 35, 42), visit.At);
    }

    [Fact]
    public async Task A_time_changed_is_written_to_the_minute_on_the_same_day()
    {
        var visit = new Visit { At = new DateTime(2026, 11, 6, 14, 35, 42) };
        var page = Shift(visit);

        await page.On(Time).Change("09:05");
        await Acting.Act(page);

        Assert.Equal(new DateTime(2026, 11, 6, 9, 5, 0), visit.At);
    }

    [Fact]
    public async Task The_time_waits_for_a_day_and_a_first_day_is_its_first_minute()
    {
        var visit = new Visit();
        var page = Page.Render(() => Form.Model(visit)[Ui.DayAndTime.Bind(() => visit.At).Live().Locale("en-US").Id("at")]);
        var before = page.Exists("[data-ui-time-picker] input[data-rask-segment=\"hour\"][disabled]");

        await page.On(Day).Change("2026-11-06");

        Assert.True(before);
        Assert.False(page.Exists("[data-ui-time-picker] input[data-rask-segment=\"hour\"][disabled]"));
        Assert.Equal(new DateTime(2026, 11, 6, 0, 0, 0), visit.At);
    }

    [Fact]
    public async Task A_day_taken_away_takes_its_time_with_it()
    {
        var visit = new Visit { At = new DateTime(2026, 11, 6, 14, 35, 0) };
        var page = Shift(visit);

        await page.On(Day).Change(string.Empty);
        await Acting.Act(page);

        Assert.Null(visit.At);
        Assert.True(page.Exists("[data-ui-time-picker] input[data-rask-segment=\"hour\"][disabled]"));
    }

    [Fact]
    public void Only_a_moment_the_model_may_leave_empty_has_the_days_clear_button()
    {
        var plan = new Plan { Starts = new DateTime(2026, 11, 6, 8, 0, 0), Due = new DateTime(2026, 11, 7, 8, 0, 0) };

        var optional = Page.Render(() => Form.Model(plan)[Ui.DayAndTime.Bind(() => plan.Starts).Locale("en-US")]).Html;
        var required = Page.Render(() => Form.Model(plan)[Ui.DayAndTime.Bind(() => plan.Due).Locale("en-US")]).Html;
        var always = Page.Render(() => Form.Model(plan)[Ui.DayAndTime.Bind(() => plan.Fixed).Locale("en-US")]).Html;

        Assert.Contains("aria-label=\"Clear\"", optional, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Clear\"", required, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Clear\"", always, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_required_moment_emptied_says_required_under_the_pair_at_the_save()
    {
        var plan = new Plan { Due = new DateTime(2026, 11, 7, 8, 0, 0) };
        var saves = 0;
        var page = Acting.Render(() => Form.Model(plan).OnSubmit(_ => saves++)[
            Ui.DayAndTime.Bind(() => plan.Due).Locale("en-US").Label("Due").Id("at")]);

        await page.On(Day).Change(string.Empty);
        await page.On("form").Submit();

        Assert.Null(plan.Due);
        Assert.Equal("The Due field is required.", page.TextOf("#at-error"));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_datetime_that_cannot_be_empty_says_so_at_once_and_keeps_its_moment()
    {
        var plan = new Plan { Fixed = new DateTime(2026, 11, 6, 8, 0, 0) };
        var saves = 0;
        var page = Acting.Render(() => Form.Model(plan).OnSubmit(_ => saves++)[
            Ui.DayAndTime.Bind(() => plan.Fixed).Locale("en-US").Label("Fixed").Id("at")]);

        await page.On(Day).Change(string.Empty);
        await page.On("form").Submit();

        Assert.Equal("This field is required.", page.TextOf("#at-error"));
        Assert.Equal(new DateTime(2026, 11, 6, 8, 0, 0), plan.Fixed);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_day_left_half_typed_says_so_under_the_pair_and_the_save_keeps_the_moment_out()
    {
        var visit = new Visit { At = new DateTime(2026, 11, 6, 14, 35, 0) };
        var saves = 0;
        var page = Shift(visit, () => saves++);

        await page.On(Day).Raise("change", HalfDate);
        await page.On("form").Submit();

        Assert.Equal("Enter a valid date.", page.TextOf("#at-error"));
        Assert.Equal("true", page.Find("#at").Attribute("aria-invalid"));
        Assert.Equal(new DateTime(2026, 11, 6, 14, 35, 0), visit.At);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_time_left_half_typed_says_so_in_hungarian_out_of_the_kits_catalog()
    {
        var visit = new Visit { At = new DateTime(2026, 11, 6, 14, 35, 0) };
        var saves = 0;
        var page = Shift(visit, () => saves++);
        var language = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hu-HU");

        await page.On(Time).Raise("change", HalfTime);
        await page.On("form").Submit();
        CultureInfo.CurrentUICulture = language;

        Assert.Equal("Érvényes időpontot adjon meg.", page.TextOf("#at-error"));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_whole_day_typed_after_a_half_one_takes_the_sentence_away_and_saves()
    {
        var visit = new Visit { At = new DateTime(2026, 11, 6, 14, 35, 0) };
        var saves = 0;
        var page = Shift(visit, () => saves++);

        await page.On(Day).Raise("change", HalfDate);
        await page.On("form").Submit();
        await page.On(Day).Change("2026-11-10");
        await page.On("form").Submit();

        Assert.Equal("", page.TextOf("#at-error"));
        Assert.Equal(new DateTime(2026, 11, 10, 14, 35, 0), visit.At);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task A_moment_kept_in_utc_stays_in_utc_when_its_day_changes()
    {
        var visit = new Visit { At = new DateTime(2026, 11, 6, 14, 35, 0, DateTimeKind.Utc) };
        var page = Shift(visit);

        await page.On(Day).Change("2026-11-08");
        await Acting.Act(page);

        Assert.Equal(DateTimeKind.Utc, visit.At!.Value.Kind);
        Assert.Equal(new DateTime(2026, 11, 8, 14, 35, 0), visit.At);
    }

    [Fact]
    public async Task A_controlled_pair_hands_the_whole_moment_to_its_parent()
    {
        var heard = new List<DateTime?>();
        var page = Page.Render(() => Ui.DayAndTime.Value((DateTime?)new DateTime(2026, 11, 6, 14, 35, 0)).OnChange(heard.Add).Locale("en-US").Id("at"));

        await page.On(Time).Change("16:00");

        Assert.Equal([new DateTime(2026, 11, 6, 16, 0, 0)], heard);
    }

    [Fact]
    public void A_pair_bound_to_anything_but_a_moment_says_what_it_binds()
    {
        var plan = new Plan();

        var thrown = Record.Exception(() => Page.Render(() => Form.Model(plan)[Ui.DayAndTime.Bind(() => plan.Name)]));

        Assert.Contains("binds a DateTime or a DateTime?", thrown?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_date_picker_is_typed_in_the_cultures_order_and_its_header_picks_month_and_year()
    {
        var picker = Ui.DatePicker.Value(Today).Typed().Locale("hu-HU").On(Today);

        var html = picker.ToHtml();

        Assert.Contains("data-rask-segments", html, StringComparison.Ordinal);
        Assert.True(
            html.IndexOf("data-rask-segment=\"year\"", StringComparison.Ordinal) < html.IndexOf("data-rask-segment=\"day\"", StringComparison.Ordinal));
        Assert.Contains("<select", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_range_is_two_fields_its_start_and_its_end_named_from_the_catalog()
    {
        var range = new UiDateRange(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6));

        var english = KitCulture.In("en-US", () => Ui.DatePicker.Range.Value(range).Typed().Locale("en-US").On(Today).ToHtml());
        var hungarian = KitCulture.In("hu-HU", () => Ui.DatePicker.Range.Value(range).Typed().Locale("hu-HU").On(Today).ToHtml());

        Assert.Equal(2, CountOf(english, "data-rask-segments=\""));
        Assert.Contains(">Start</label>", english, StringComparison.Ordinal);
        Assert.Contains(">End</label>", english, StringComparison.Ordinal);
        Assert.Contains(">Kezdet</label>", hungarian, StringComparison.Ordinal);
        Assert.Contains(">V&#xE9;ge</label>", hungarian, StringComparison.Ordinal);
    }

    private static int CountOf(string html, string text)
    {
        var count = 0;
        for (var at = html.IndexOf(text, StringComparison.Ordinal); at >= 0; at = html.IndexOf(text, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private sealed class Visit
    {
        public DateTime? At { get; set; }
    }

    private sealed class Plan
    {
        public DateTime? Starts { get; set; }

        [Required]
        public DateTime? Due { get; set; }

        public DateTime Fixed { get; set; }

        public string Name { get; set; } = "";
    }
}
