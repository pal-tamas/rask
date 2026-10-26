using System.Text.RegularExpressions;
using Rask.Testing;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.DatetimePicker</c> — Flux UI's datetime picker — for a moment and for a range: what the button shows, what
///     the browser announces and how it is read, and the markup the popup's runtime and a screen reader depend on.
/// </summary>
/// <remarks>
///     The page's first paint and the value it hears. What a reader does in the popup is the browser's
///     (<c>rask-calendar.ts</c>, <c>RuntimeHookDatetimeTests</c>); <c>scripts/flux/parity-date.mjs datetime-picker</c>
///     holds the look to Flux's live page.
/// </remarks>
public sealed partial class UiDatetimePickerTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    // The one field the page hears the choice through, first in the picker.
    private const string Field = "[data-ui-datetime-picker] > input[type=\"hidden\"]";

    private static DateTime Jan(int day, int hour, int minute) => new(2026, 1, day, hour, minute, 0);

    private static UiDatetimePicker One(DateTime value = default) =>
        Ui.DatetimePicker.Value(value).Locale("en-US").On(Today).At(new TimeOnly(13, 0));

    private static UiDatetimePickerRange Ranged(UiDateTimeRange value = default) =>
        Ui.DatetimePicker.Range.Value(value).Locale("en-US").On(Today).At(new TimeOnly(13, 0));

    private sealed class Booking
    {
        public DateTime StartsAt { get; set; }

        public DateTime? EndsAt { get; set; }

        public UiDateTimeRange Window { get; set; }
    }

    [Fact]
    public void A_picker_shows_its_placeholder_until_a_moment_is_chosen()
    {
        var single = One().ToHtml();

        var range = Ranged().ToHtml();

        Assert.Contains(">Select a date and time</span>", single, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Select a date and time\"", single, StringComparison.Ordinal);
        Assert.Contains(">Select a date and time range</span>", range, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chosen_moment_reads_as_the_date_then_a_grey_at_then_the_time()
    {
        var picker = One(Jan(27, 10, 30));

        var html = picker.ToHtml();

        Assert.Contains(">Jan 27, 2026<span class=\"text-zinc-400\"> at </span>10:30 AM</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_time_follows_the_format_asked_for_and_the_date_the_locale()
    {
        var moment = Jan(23, 18, 30);

        var twentyFour = One(moment).TimeFormat(Ui.DatetimePickerTimeFormat.TwentyFourHour).ToHtml();
        var french = Ui.DatetimePicker.Value(moment).Locale("fr-FR").On(Today).ToHtml();

        Assert.Contains(">Jan 23, 2026<span class=\"text-zinc-400\"> at </span>18:30</div>", twentyFour, StringComparison.Ordinal);
        Assert.Contains(">23 janv. 2026<span class=\"text-zinc-400\"> &#xE0; </span>18:30</div>", french, StringComparison.Ordinal);
    }

    [Fact]
    public void The_word_between_the_date_and_the_time_follows_the_pickers_locale_and_not_the_apps_language()
    {
        var moment = new DateTime(2026, 10, 27, 10, 30, 0);

        var hungarianPicker = KitCulture.In("en-US", () => Ui.DatetimePicker.Value(moment).Locale("hu-HU").On(Today).ToHtml());
        var englishPicker = KitCulture.In("hu-HU", () => Ui.DatetimePicker.Value(moment).Locale("en-US").On(Today).ToHtml());

        Assert.Contains(">2026. okt. 27. 10:30</div>", hungarianPicker, StringComparison.Ordinal);
        Assert.Contains(">Oct 27, 2026<span class=\"text-zinc-400\"> at </span>10:30 AM</div>", englishPicker, StringComparison.Ordinal);
    }

    [Fact]
    public void Moments_taken_straight_from_a_query_are_unavailable_slots_of_one_minute()
    {
        var bookings = new[] { (StartsAt: Jan(16, 10, 0), Room: "A"), (StartsAt: Jan(16, 11, 30), Room: "B") };

        var html = One(Jan(16, 9, 0)).Unavailable(bookings.Select(b => b.StartsAt)).ToHtml();
        var column = Regex.Match(html, "data-ui-datetime-picker-times[^>]*>(.*?)</div></div></div>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Equal(["10:00", "11:30"], Regex.Matches(column, "data-time=\"([^\"]+)\"[^>]*disabled").Select(m => m.Groups[1].Value));
    }

    [Theory]
    [InlineData(27, 9, 0, 32, 17, 0, "Jan 27, 9:00 AM", "Feb 1, 5:00 PM")]
    [InlineData(22, 14, 0, 22, 16, 30, "Jan 22, 2:00", "4:30 PM")]
    [InlineData(22, 9, 0, 22, 16, 30, "Jan 22, 9:00 AM", "4:30 PM")]
    public void A_range_drops_this_years_year_and_writes_one_days_date_and_period_once(
        int startDay, int startHour, int startMinute, int endDay, int endHour, int endMinute, string before, string after)
    {
        var range = new UiDateTimeRange(
            new DateTime(2026, 1, 1, startHour, startMinute, 0).AddDays(startDay - 1),
            new DateTime(2026, 1, 1, endHour, endMinute, 0).AddDays(endDay - 1));

        var html = Ranged(range).ToHtml();

        Assert.Contains(">" + before + "<span class=\"text-zinc-400\"> &#x2013; </span>" + after + "</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_range_outside_this_year_keeps_both_years()
    {
        var range = new UiDateTimeRange(new DateTime(2026, 12, 30, 9, 0, 0), new DateTime(2027, 1, 2, 16, 30, 0));

        var html = Ranged(range).ToHtml();

        Assert.Contains(">Dec 30, 2026, 9:00 AM<span class=\"text-zinc-400\"> &#x2013; </span>Jan 2, 2027, 4:30 PM</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_button_names_the_dialog_it_opens_and_is_no_combobox()
    {
        var html = One().ToHtml();

        var popup = Regex.Match(html, "popovertarget=\"([^\"]+)\"").Groups[1].Value;

        Assert.StartsWith("ui-datetime-picker-", popup, StringComparison.Ordinal);
        Assert.Contains("aria-haspopup=\"dialog\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"" + popup + "\"", html, StringComparison.Ordinal);
        Assert.Contains("<dialog id=\"" + popup + "\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"combobox\" class=\"flex items-center w-full px-3 py-2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_moment_the_browser_announces_is_written_with_the_next_action_and_shown_on_the_button()
    {
        var model = new Booking();
        var page = Acting.Render(() => Ui.DatetimePicker.Bind(() => model.StartsAt).Locale("en-US").On(Today));

        var waits = page.Find(Field).Attribute("data-rask-bind-on");
        await page.On(Field).Change("2026-01-20T09:45");
        await Acting.Act(page);

        Assert.Equal("action", waits);
        Assert.Equal(Jan(20, 9, 45), model.StartsAt);
        Assert.Contains(">Jan 20, 2026<span class=\"text-zinc-400\"> at </span>9:45 AM</div>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_moment_is_carried_as_a_datetime_local_field_holds_it()
    {
        var html = One(new DateTime(2026, 1, 20, 9, 45, 59)).ToHtml();

        Assert.Contains("value=\"2026-01-20T09:45\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_live_picker_writes_the_moment_as_it_is_chosen()
    {
        var model = new Booking();
        var page = Acting.Render(() => Ui.DatetimePicker.Bind(() => model.EndsAt).Live().Locale("en-US").On(Today));

        await page.On(Field).Input("2026-01-21T17:00");
        await Acting.Act(page);

        Assert.Equal(Jan(21, 17, 0), model.EndsAt);
    }

    [Fact]
    public async Task A_range_is_written_whole_and_one_that_ends_before_it_starts_is_not_read()
    {
        var model = new Booking();
        var page = Acting.Render(() => Ui.DatetimePicker.Range.Bind(() => model.Window).Locale("en-US").On(Today));

        await page.On(Field).Change("2026-01-20T17:00/2026-01-20T09:00");
        await Acting.Act(page);
        var backwards = model.Window;
        await page.On(Field).Change("2026-01-20T09:00/2026-01-22T17:00");
        await Acting.Act(page);

        Assert.Equal(default, backwards);
        Assert.Equal(new UiDateTimeRange(Jan(20, 9, 0), Jan(22, 17, 0)), model.Window);
    }

    [Fact]
    public void The_column_lists_every_interval_between_the_min_and_max_time_and_marks_the_chosen_one()
    {
        var html = One(Jan(20, 10, 0)).Interval(60).MinTime(new TimeOnly(9, 0)).MaxTime(new TimeOnly(12, 0)).ToHtml();

        var column = Regex.Match(html, "data-ui-datetime-picker-times[^>]*>(.*?)</div></div></div>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Equal(["09:00", "10:00", "11:00", "12:00"], Regex.Matches(column, "data-time=\"([^\"]+)\"").Select(m => m.Groups[1].Value));
        Assert.Matches("<button[^>]*data-time=\"10:00\" data-selected=\"\"[^>]*tabindex=\"0\"[^>]*aria-selected=\"true\"", column);
        Assert.Equal(3, Regex.Matches(column, "aria-selected=\"false\"").Count);
    }

    [Fact]
    public void With_nothing_chosen_the_column_opens_on_the_last_time_not_after_now()
    {
        var html = One().ToHtml();

        var column = Regex.Match(html, "data-ui-datetime-picker-times[^>]*>(.*?)</div></div></div>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Matches("data-time=\"13:00\" data-rask-time-start=\"\"[^>]*tabindex=\"0\"", column);
        Assert.Single(Regex.Matches(column, "tabindex=\"0\""));
    }

    [Fact]
    public void On_the_mins_day_the_earlier_times_cannot_be_picked()
    {
        var html = One(Jan(15, 14, 0)).Min(Jan(15, 13, 0)).Max(Jan(20, 17, 0)).ToHtml();

        var column = Regex.Match(html, "data-ui-datetime-picker-times[^>]*>(.*?)</div></div></div>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Matches("data-time=\"12:30\"[^>]*disabled", column);
        Assert.DoesNotMatch("data-time=\"13:00\"[^>]*disabled", column);
        Assert.Matches("aria-label=\"Wednesday, January 14, 2026\"[^>]*disabled|disabled[^>]*aria-label=\"Wednesday, January 14, 2026\"", html);
    }

    [Fact]
    public void An_unavailable_day_is_off_in_the_calendar_and_unavailable_times_in_the_column()
    {
        var picker = One(Jan(16, 9, 0)).Unavailable([
            new DateOnly(2026, 1, 17),
            new UiTimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0)),
            Jan(16, 10, 0),
            UiDatetimeSlot.On(new DateOnly(2026, 1, 16), new TimeOnly(15, 0), new TimeOnly(16, 0)),
        ]);

        var html = picker.ToHtml();
        var column = Regex.Match(html, "data-ui-datetime-picker-times[^>]*>(.*?)</div></div></div>", RegexOptions.Singleline).Groups[1].Value;
        var off = Regex.Matches(column, "data-time=\"([^\"]+)\"[^>]*disabled").Select(m => m.Groups[1].Value);

        Assert.Equal(["10:00", "12:00", "12:30", "13:00", "15:00", "15:30", "16:00"], off);
        Assert.Matches("data-date=\"2026-01-17\" data-unavailable", html);
    }

    [Fact]
    public void The_column_is_the_desktops_and_a_phone_picks_the_time_in_a_field_under_the_months()
    {
        var html = One().ToHtml();

        Assert.Contains("max-sm:hidden flex flex-col w-36", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-datetime-picker-time=\"single\"", html, StringComparison.Ordinal);
        Assert.Contains("hidden max-sm:flex items-center gap-3", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-time-list=\"\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_confirmation_the_footer_is_a_phones_and_with_it_every_widths()
    {
        var plain = One().ToHtml();

        var confirming = One().WithConfirmation().ToHtml();
        var typed = One().Time(Ui.DatetimePickerTime.Input).ToHtml();

        Assert.Contains("hidden max-sm:flex items-center justify-end", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden max-sm:flex items-center justify-end", confirming, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden max-sm:flex items-center justify-end", typed, StringComparison.Ordinal);
        Assert.Contains("\"confirms\":true", typed.Replace("&quot;", "\"", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Select_waits_disabled_until_a_whole_moment_is_there()
    {
        var unset = One().ToHtml();

        var set = One(Jan(20, 9, 0)).ToHtml();

        Assert.Matches("data-ui-datetime-picker-select[^>]* disabled [^>]*>Select</button>", unset);
        Assert.DoesNotMatch("data-ui-datetime-picker-select[^>]* disabled ", set);
    }

    [Fact]
    public void Typed_times_are_hour_and_minute_parts_with_the_whole_time_in_one_field()
    {
        var html = One(Jan(20, 14, 47)).Time(Ui.DatetimePickerTime.Input).ToHtml();

        Assert.Contains("data-rask-segment=\"hour\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-segment=\"minute\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-segment=\"meridiem\"", html, StringComparison.Ordinal);
        Assert.Matches("data-rask-segments=\"\"[^>]*>.*<input data-rask-unguarded type=\"hidden\" value=\"14:47\"", html);
        Assert.DoesNotContain("data-ui-datetime-picker-times", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_range_draws_a_start_and_an_end_field_and_the_message_it_shows_when_they_cross()
    {
        var html = Ranged().ToHtml();

        Assert.Contains("data-ui-datetime-picker-time=\"start\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-datetime-picker-time=\"end\"", html, StringComparison.Ordinal);
        Assert.Matches("data-ui-datetime-picker-error=\"\"[^>]*hidden=\"\"[^>]*>End must be after start</div>", html);
        Assert.Contains(">Select range</button>", html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(html, "role=\"grid\"").Count);
    }

    [Fact]
    public void A_trigger_of_the_pages_own_opens_the_dialog_and_lets_the_picker_be_its_width()
    {
        var html = One().Trigger(Ui.Button.Icon(Ui.IconName.CalendarDays)["Schedule"]).ToHtml();

        var popup = Regex.Match(html, "<dialog id=\"([^\"]+)\"").Groups[1].Value;

        Assert.Matches("<button[^>]*aria-haspopup=\"dialog\"[^>]*aria-controls=\"" + popup + "\"", html);
        Assert.Contains("popovertarget=\"" + popup + "\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("max-w-64", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-datetime-picker-button", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_clear_button_empties_a_nullable_moment()
    {
        var model = new Booking { EndsAt = Jan(20, 9, 0) };
        var page = Acting.Render(() => Ui.DatetimePicker.Bind(() => model.EndsAt).Clearable().Locale("en-US").On(Today));

        await page.On("[aria-label=\"Clear date and time\"]").Click();
        await Acting.Act(page);

        Assert.Null(model.EndsAt);
    }

    [Fact]
    public void A_mode_set_against_what_is_bound_says_which_to_bind()
    {
        var picker = One().Mode(Ui.DatetimePickerMode.Range);

        var thrown = Assert.Throws<InvalidOperationException>(() => picker.ToHtml());

        Assert.Contains("Ui.DatetimePicker.Range", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_runtime_is_told_the_times_rules_and_their_words()
    {
        var html = One().DefaultTime(new TimeOnly(9, 0)).Min(Jan(15, 13, 0)).OpenTo(new TimeOnly(8, 0))
            .Unavailable([new UiTimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0))]).ToHtml().Replace("&quot;", "\"", StringComparison.Ordinal);

        Assert.Contains("\"time\":{\"input\":false,\"pattern\":\"h:mm tt\"", html, StringComparison.Ordinal);
        Assert.Contains("\"min\":\"2026-01-15T13:00\"", html, StringComparison.Ordinal);
        Assert.Contains("\"unavailable\":[\"12:00-13:00\"]", html, StringComparison.Ordinal);
        Assert.Contains("\"defaults\":[540,540]", html, StringComparison.Ordinal);
        Assert.Contains("\"openTo\":480", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-12-25", "2026-12-25")]
    [InlineData("12:00", "12:00")]
    [InlineData("12:00-13:00", "12:00-13:00")]
    [InlineData("2026-10-14T10:00", "2026-10-14T10:00")]
    [InlineData("2026-10-14T15:00-16:00", "2026-10-14T15:00-16:00")]
    public void A_slot_reads_and_writes_as_Flux_writes_it(string text, string written)
    {
        var parsed = UiDatetimeSlot.TryParse(text, null, out var slot);

        var again = slot.ToString();

        Assert.True(parsed);
        Assert.Equal(written, again);
    }

    [Fact]
    public void A_datetime_range_reads_back_what_it_writes_and_knows_its_ends()
    {
        var range = new UiDateTimeRange(Jan(14, 22, 0), Jan(15, 2, 0));

        var text = range.ToString();
        var read = UiDateTimeRange.Parse(text, null);

        Assert.Equal("2026-01-14T22:00..2026-01-15T02:00", text);
        Assert.Equal(range, read);
        Assert.True(read.HasStart && read.HasEnd);
        Assert.False(default(UiDateTimeRange).HasStart);
        Assert.True(range.Contains(Jan(15, 0, 30)));
    }
}
