using System.Globalization;
using System.Text.RegularExpressions;
using Rask.Testing;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.Calendar</c> — Flux UI's calendar — for a day, several days and a range: what a pick writes, what
///     the keyboard does, and the markup a screen reader depends on.
/// </summary>
/// <remarks>
///     The page's first paint and the value it hears, on a pinned day so a month has the weeks the test counts.
///     What the keys, a press and the pointer do is the browser's (<c>rask-calendar.ts</c>), asserted in
///     <c>RuntimeHookCalendarTests</c>. How it looks is <c>scripts/flux/parity.mjs calendar</c>'s to hold.
/// </remarks>
public partial class UiCalendarTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static string Day(DateOnly day) => "button[aria-label=\"" + day.ToString("D", English) + "\"]";

    private static UiCalendar Single(DateOnly value = default) => Ui.Calendar.Value(value).Locale("en-US").On(Today);

    private static UiCalendarRange Ranged(UiDateRange value = default) => Ui.Calendar.Range.Value(value).Locale("en-US").On(Today);

    // The day Tab lands on: the one button in the grid with tabindex 0.
    private static string[] TabStops(string html) =>
        [.. Regex.Matches(html, "tabindex=\"0\"[^>]*aria-label=\"([^\"]+)\"").Select(match => match.Groups[1].Value)];

    // The one field the page hears the choice through, first in the calendar.
    private const string Field = "[data-ui-calendar] > input[type=\"hidden\"]";

    private sealed class Trip
    {
        public DateOnly Day { get; set; }

        public DateOnly? Due { get; set; }

        public List<DateOnly> DaysOff { get; set; } = [];

        public UiDateRange Stay { get; set; }
    }

    [Fact]
    public void A_month_is_a_grid_of_cells_named_by_their_full_date()
    {
        var calendar = Single();

        var html = calendar.ToHtml();

        Assert.Contains("data-ui-calendar", html, StringComparison.Ordinal);
        Assert.Contains("role=\"grid\"", html, StringComparison.Ordinal);
        Assert.Equal(35, Regex.Count(html, "role=\"gridcell\""));
        Assert.Contains("aria-label=\"Thursday, January 15, 2026\"", html, StringComparison.Ordinal);
        Assert.Contains(">January 2026<", html, StringComparison.Ordinal);
        Assert.Contains("data-date=\"2025-12-28\" data-outside", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_chosen_day_is_the_one_selected_cell()
    {
        var calendar = Single(Jan(20));

        var html = calendar.ToHtml();

        Assert.Equal(1, Regex.Count(html, "aria-selected=\"true\""));
        Assert.Contains("data-date=\"2026-01-20\" data-selected", html, StringComparison.Ordinal);
        Assert.Equal(34, Regex.Count(html, "aria-selected=\"false\""));
    }

    [Fact]
    public async Task The_day_the_browser_announces_is_written_with_the_next_action_and_nothing_announced_clears_it()
    {
        var trip = new Trip();
        var page = Acting.Render(() => Ui.Calendar.Bind(() => trip.Day).Locale("en-US").On(Today));

        var waits = page.Find(Field).Attribute("data-rask-bind-on");
        await page.On(Field).Change("2026-01-20");
        var held = trip.Day;
        await Acting.Act(page);
        var picked = trip.Day;
        await page.On(Field).Change("");
        await Acting.Act(page);

        Assert.Equal("action", waits);
        Assert.Equal(default, held);
        Assert.Equal(Jan(20), picked);
        Assert.Equal(default, trip.Day);
    }

    [Fact]
    public async Task A_nullable_binding_takes_the_picked_day()
    {
        var trip = new Trip();
        var page = Acting.Render(() => Ui.Calendar.Bind(() => trip.Due).Locale("en-US").On(Today));

        await page.On(Field).Change("2026-01-09");
        await Acting.Act(page);

        Assert.Equal(Jan(9), trip.Due);
        Assert.Equal("2026-01-09", page.Find(Field).Attribute("value"));
    }

    [Fact]
    public void An_unbound_calendar_keeps_the_pick_in_the_browser_and_the_page_hears_nothing()
    {
        var page = Page.Render(() => Single(Jan(8)));

        var field = page.Find(Field);

        Assert.Equal("2026-01-08", field.Attribute("value"));
        Assert.Null(field.Attribute("data-rask-on-change"));
        Assert.Null(field.Attribute("data-rask-on-input"));
    }

    [Fact]
    public async Task A_collection_of_days_takes_what_the_browser_announces_in_date_order()
    {
        var trip = new Trip();
        var page = Acting.Render(() => Ui.Calendar.Multiple.Bind(() => trip.DaysOff).Locale("en-US").On(Today));

        await page.On(Field).Change("2026-01-20,2026-01-05");
        await Acting.Act(page);
        var two = trip.DaysOff.ToList();
        await page.On(Field).Change("2026-01-05");
        await Acting.Act(page);

        Assert.Equal([Jan(5), Jan(20)], two);
        Assert.Equal([Jan(5)], trip.DaysOff);
        Assert.Equal("2026-01-05", page.Find(Field).Attribute("value"));
    }

    [Fact]
    public async Task A_range_is_written_whole_and_text_that_is_no_range_is_not()
    {
        var trip = new Trip();
        var page = Acting.Render(() => Ui.Calendar.Range.Bind(() => trip.Stay).Locale("en-US").On(Today));

        await page.On(Field).Change("2026-01-10");
        await Acting.Act(page);
        var half = trip.Stay;
        await page.On(Field).Change("2026-01-10/2026-01-13");
        await Acting.Act(page);

        Assert.Equal(default, half);
        Assert.Equal(new UiDateRange(Jan(10), Jan(13)), trip.Stay);
        Assert.Equal("2026-01-10/2026-01-13", page.Find(Field).Attribute("value"));
    }

    [Fact]
    public void A_range_marks_its_ends_and_every_day_between()
    {
        var calendar = Ranged(new UiDateRange(Jan(6), Jan(9)));

        var html = calendar.ToHtml();

        Assert.Equal(4, Regex.Count(html, "data-in-range"));
        Assert.Equal(4, Regex.Count(html, "aria-selected=\"true\""));
        Assert.DoesNotContain("aria-selected=\"false\"", html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(html, "role=\"grid\""));
    }

    [Fact]
    public void One_day_is_in_the_tab_order_the_chosen_one_then_today_then_the_first()
    {
        var unset = TabStops(Single().ToHtml());

        var chosen = TabStops(Single(Jan(20)).ToHtml());
        var elsewhere = TabStops(Single().OpenTo(new DateOnly(2027, 11, 1)).ToHtml());

        Assert.Equal(["Thursday, January 15, 2026"], unset);
        Assert.Equal(["Tuesday, January 20, 2026"], chosen);
        Assert.Equal(["Monday, November 1, 2027"], elsewhere);
    }

    [Fact]
    public void The_locale_names_the_month_the_weekdays_and_the_days()
    {
        var calendar = Ui.Calendar.Value(default(DateOnly)).Locale("ja-JP").On(Today);

        var html = System.Net.WebUtility.HtmlDecode(calendar.ToHtml());

        Assert.Contains(">2026年1月<", html, StringComparison.Ordinal);
        Assert.Contains(">日<", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"2026年1月15日", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_week_starts_where_the_locale_starts_it_unless_told()
    {
        var american = Single().ToHtml();

        var german = Ui.Calendar.Value(default(DateOnly)).Locale("de-DE").On(Today).ToHtml();
        var told = Single().StartDay(DayOfWeek.Monday).ToHtml();

        Assert.StartsWith("Su", Regex.Match(american, "<th[^>]*><div[^>]*>([^<]+)<").Groups[1].Value, StringComparison.Ordinal);
        Assert.StartsWith("Mo", Regex.Match(german, "<th[^>]*><div[^>]*>([^<]+)<").Groups[1].Value, StringComparison.Ordinal);
        Assert.StartsWith("Mo", Regex.Match(told, "<th[^>]*><div[^>]*>([^<]+)<").Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("data-date=\"2025-12-29\" data-outside", told, StringComparison.Ordinal);
    }

    [Fact]
    public void A_day_outside_min_and_max_is_disabled()
    {
        var html = Single().Min(Jan(10)).Max(Jan(20)).ToHtml();

        Assert.Matches("data-date=\"2026-01-09\"[^>]*aria-disabled=\"true\"", html);
        Assert.Matches("data-date=\"2026-01-21\"[^>]*aria-disabled=\"true\"", html);
        Assert.DoesNotMatch("data-date=\"2026-01-10\"[^>]*aria-disabled", html);
        Assert.Matches("aria-label=\"Friday, January 9, 2026\"[^>]*\\sdisabled", html);
    }

    [Fact]
    public void A_month_step_is_disabled_when_the_whole_month_that_way_is_out_of_reach()
    {
        var calendar = Single().Min(Jan(10)).Max(Jan(20));

        var html = calendar.ToHtml();

        Assert.Matches("aria-label=\"Previous month\"[^>]*\\sdisabled", html);
        Assert.Matches("aria-label=\"Next month\"[^>]*\\sdisabled", html);
        Assert.DoesNotMatch("aria-label=\"Next month\"[^>]*\\sdisabled", Single().ToHtml());
    }

    [Fact]
    public void An_unavailable_day_is_marked_and_disabled()
    {
        var calendar = Single().Unavailable([Jan(14), Jan(16)]);

        var html = calendar.ToHtml();

        Assert.Equal(2, Regex.Count(html, "data-unavailable"));
        Assert.Matches("data-date=\"2026-01-14\"[^>]*data-unavailable[^>]*aria-disabled=\"true\"", html);
    }

    [Fact]
    public void The_grid_keeps_its_keys_from_the_page_and_the_browser_draws_it_from_the_first_paint_on()
    {
        var html = Single().ToHtml();

        var looked = Single().Static().ToHtml();

        // Flux's list, measured: the arrows, Home, End and the paging keys — not Space, which it lets scroll.
        Assert.Contains("data-rask-contain-keys=\"Arrows Home End PageUp PageDown\"", html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(html, "data-rask-opaque"));
        Assert.Contains("data-rask-calendar=\"{&quot;mode&quot;:&quot;single&quot;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-", html, StringComparison.Ordinal);
        Assert.Matches("<button[^>]*tabindex=\"0\"[^>]*aria-label=\"Thursday, January 15, 2026\"", html);
        Assert.DoesNotContain("data-rask-contain-keys", looked, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_months_that_draw_the_same_day_twice_have_one_tab_stop_in_the_days_own_month()
    {
        var html = Ranged(new UiDateRange(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 3)))
            .OpenTo(new DateOnly(2026, 3, 1)).ForceOpenTo().ToHtml();

        // April 1 closes March's last week as a neighbour's day and stands in April's own first week.
        Assert.Equal(2, Regex.Matches(html, "aria-label=\"Wednesday, April 1, 2026\"").Count);
        Assert.Equal(["Wednesday, April 1, 2026"], TabStops(html));
    }

    [Fact]
    public async Task A_selectable_header_offers_the_months_and_a_century_of_years()
    {
        var page = Page.Render(() => Single().SelectableHeader());

        var html = page.Render();

        Assert.Equal(2, Regex.Count(html, "<select"));
        Assert.Equal(12 + 111, Regex.Count(html, "<option"));
        Assert.Contains(">1926<", html, StringComparison.Ordinal);
        Assert.Contains(">2036<", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-calendar-part=\"month\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Week_numbers_are_the_iso_week_of_each_rows_thursday()
    {
        var calendar = Single().WeekNumbers();

        var html = calendar.ToHtml();

        Assert.Contains(">#<", html, StringComparison.Ordinal);
        Assert.Equal(["1", "2", "3", "4", "5"], Regex.Matches(html, "<tr[^>]*><td(?![^>]*role)[^>]*>(\\d+)<").Select(match => match.Groups[1].Value));
    }

    [Fact]
    public void Fixed_weeks_always_draws_six()
    {
        var calendar = Single().FixedWeeks();

        var html = calendar.ToHtml();

        Assert.Equal(42, Regex.Count(html, "role=\"gridcell\""));
    }

    [Fact]
    public void A_static_calendar_has_no_buttons_and_names_its_cells()
    {
        var calendar = Single(Jan(20)).Static().Navigation(false);

        var html = calendar.ToHtml();

        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
        Assert.Matches("<td[^>]*aria-label=\"Tuesday, January 20, 2026\"[^>]*aria-selected=\"true\"", html);
    }

    [Fact]
    public void Months_draws_that_many_side_by_side_and_a_range_draws_two()
    {
        var three = Single().Months(3).ToHtml();

        var range = Ranged().ToHtml();

        Assert.Equal(3, Regex.Count(three, "role=\"grid\""));
        Assert.Contains(">March 2026<", three, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(range, "role=\"grid\""));
    }

    [Fact]
    public void Open_to_yields_to_the_chosen_day_unless_forced()
    {
        var march = new DateOnly(2026, 3, 1);

        var unset = Single().OpenTo(march).ToHtml();
        var chosen = Single(Jan(20)).OpenTo(march).ToHtml();
        var forced = Single(Jan(20)).OpenTo(march).ForceOpenTo().ToHtml();

        Assert.Contains(">March 2026<", unset, StringComparison.Ordinal);
        Assert.Contains(">January 2026<", chosen, StringComparison.Ordinal);
        Assert.Contains(">March 2026<", forced, StringComparison.Ordinal);
    }

    [Fact]
    public void Navigation_off_draws_no_month_steps()
    {
        var calendar = Single().Navigation(false);

        var html = calendar.ToHtml();

        Assert.DoesNotContain("Next month", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Previous month", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_size_sets_the_cell()
    {
        var small = Single().Xs.ToHtml();

        var large = Single().Xl.ToHtml();

        Assert.Contains("size-9", small, StringComparison.Ordinal);
        Assert.Contains("size-14", large, StringComparison.Ordinal);
        Assert.Contains("size-11", Single().ToHtml(), StringComparison.Ordinal);
    }
}
