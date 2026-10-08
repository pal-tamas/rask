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
///     Driven through the real handlers with <c>Page</c>, on a pinned day so a month has the weeks the test
///     counts. How it looks is <c>scripts/flux/parity.mjs calendar</c>'s to hold.
/// </remarks>
public partial class UiCalendarTests : global::Rask.Core.RaskMarkup
{
    private const string Months = "[data-ui-calendar-months]";

    private static readonly DateOnly Today = new(2026, 1, 15);

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static string Day(DateOnly day) => "button[aria-label=\"" + day.ToString("D", English) + "\"]";

    private static UiCalendar Single(DateOnly value = default) => Ui.Calendar.Value(value).Locale("en-US").On(Today);

    private static UiCalendarRange Ranged(UiDateRange value = default) => Ui.Calendar.Range.Value(value).Locale("en-US").On(Today);

    // The day Tab lands on: the one button in the grid with tabindex 0.
    private static string[] TabStops(string html) =>
        [.. Regex.Matches(html, "tabindex=\"0\"[^>]*aria-label=\"([^\"]+)\"").Select(match => match.Groups[1].Value)];

    private static Task Press(Page page, string key) => page.On(Months).Raise("keydown", "{\"key\":\"" + key + "\"}");

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
    public async Task A_click_writes_the_day_and_a_second_click_clears_it()
    {
        var trip = new Trip();
        var page = Page.Render(() => Ui.Calendar.Bind(() => trip.Day).Locale("en-US").On(Today));

        await page.On(Day(Jan(20))).Click();
        var picked = trip.Day;
        await page.On(Day(Jan(20))).Click();

        Assert.Equal(Jan(20), picked);
        Assert.Equal(default, trip.Day);
    }

    [Fact]
    public async Task A_nullable_binding_takes_the_picked_day()
    {
        var trip = new Trip();
        var page = Page.Render(() => Ui.Calendar.Bind(() => trip.Due).Locale("en-US").On(Today));

        await page.On(Day(Jan(9))).Click();

        Assert.Equal(Jan(9), trip.Due);
        Assert.Contains("data-date=\"2026-01-09\" data-selected", page.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unbound_calendar_keeps_the_pick_itself()
    {
        var page = Page.Render(() => Single());

        await page.On(Day(Jan(8))).Click();

        Assert.Contains("data-date=\"2026-01-08\" data-selected", page.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_collection_of_days_takes_each_click_and_gives_one_back_on_the_second()
    {
        var trip = new Trip();
        var page = Page.Render(() => Ui.Calendar.Multiple.Bind(() => trip.DaysOff).Locale("en-US").On(Today));

        await page.On(Day(Jan(20))).Click();
        await page.On(Day(Jan(5))).Click();
        var two = trip.DaysOff.ToList();
        await page.On(Day(Jan(20))).Click();

        Assert.Equal([Jan(5), Jan(20)], two);
        Assert.Equal([Jan(5)], trip.DaysOff);
    }

    [Fact]
    public async Task A_range_is_written_whole_on_its_second_click()
    {
        var trip = new Trip();
        var page = Page.Render(() => Ui.Calendar.Range.Bind(() => trip.Stay).Locale("en-US").On(Today));

        await page.On(Day(Jan(10))).Click();
        var held = trip.Stay;
        var waiting = page.Render();
        await page.On(Day(Jan(13))).Click();

        Assert.Equal(default, held);
        Assert.Matches("data-date=\"2026-01-10\"[^>]*data-start", waiting);
        Assert.Equal(new UiDateRange(Jan(10), Jan(13)), trip.Stay);
    }

    [Fact]
    public async Task A_click_before_a_waiting_start_begins_the_range_again_from_there()
    {
        var trip = new Trip();
        var page = Page.Render(() => Ui.Calendar.Range.Bind(() => trip.Stay).Locale("en-US").On(Today));

        await page.On(Day(Jan(20))).Click();
        await page.On(Day(Jan(12))).Click();
        var restarted = trip.Stay;
        await page.On(Day(Jan(14))).Click();

        Assert.Equal(default, restarted);
        Assert.Equal(new UiDateRange(Jan(12), Jan(14)), trip.Stay);
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
    public async Task A_range_limit_rules_out_days_too_near_and_too_far_from_the_start()
    {
        var page = Page.Render(() => Ranged().MinRange(3).MaxRange(5));

        await page.On(Day(Jan(10))).Click();
        var html = page.Render();

        Assert.Matches("data-date=\"2026-01-09\"[^>]*aria-disabled=\"true\"", html);
        Assert.Matches("data-date=\"2026-01-11\"[^>]*aria-disabled=\"true\"", html);
        Assert.DoesNotMatch("data-date=\"2026-01-12\"[^>]*aria-disabled", html);
        Assert.DoesNotMatch("data-date=\"2026-01-14\"[^>]*aria-disabled", html);
        Assert.Matches("data-date=\"2026-01-15\"[^>]*aria-disabled=\"true\"", html);
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
    public async Task A_day_outside_min_and_max_is_disabled_and_takes_no_click()
    {
        var trip = new Trip();
        var page = Page.Render(() => Ui.Calendar.Bind(() => trip.Day).Min(Jan(10)).Max(Jan(20)).Locale("en-US").On(Today));

        var html = page.Render();
        await page.On(Day(Jan(12))).Click();

        Assert.Matches("data-date=\"2026-01-09\"[^>]*aria-disabled=\"true\"", html);
        Assert.Matches("data-date=\"2026-01-21\"[^>]*aria-disabled=\"true\"", html);
        Assert.DoesNotMatch("data-date=\"2026-01-10\"[^>]*aria-disabled", html);
        Assert.Matches("aria-label=\"Friday, January 9, 2026\"[^>]*\\sdisabled", html);
        Assert.Equal(Jan(12), trip.Day);
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
    public async Task The_arrows_walk_days_and_weeks()
    {
        var page = Page.Render(() => Single());

        await Press(page, "ArrowRight");
        var right = TabStops(page.Render());
        await Press(page, "ArrowDown");
        var down = TabStops(page.Render());
        await Press(page, "ArrowLeft");
        await Press(page, "ArrowUp");

        Assert.Equal(["Friday, January 16, 2026"], right);
        Assert.Equal(["Friday, January 23, 2026"], down);
        Assert.Equal(["Thursday, January 15, 2026"], TabStops(page.Render()));
    }

    [Fact]
    public async Task An_arrow_past_the_drawn_weeks_takes_the_view_with_it()
    {
        var page = Page.Render(() => Single(Jan(31)));

        await Press(page, "ArrowRight");
        var html = page.Render();

        Assert.Contains(">February 2026<", html, StringComparison.Ordinal);
        Assert.Equal(["Sunday, February 1, 2026"], TabStops(html));
    }

    [Fact]
    public async Task An_arrow_steps_over_an_unavailable_day_and_stops_at_max()
    {
        var page = Page.Render(() => Single().Unavailable([Jan(16)]).Max(Jan(17)));

        await Press(page, "ArrowRight");
        var over = TabStops(page.Render());
        await Press(page, "ArrowRight");
        await Press(page, "ArrowDown");

        Assert.Equal(["Saturday, January 17, 2026"], over);
        Assert.Equal(["Saturday, January 17, 2026"], TabStops(page.Render()));
    }

    [Theory]
    [InlineData("PageDown", "February 2026")]
    [InlineData("End", "February 2026")]
    [InlineData("PageUp", "December 2025")]
    [InlineData("Home", "December 2025")]
    public async Task The_page_keys_and_home_and_end_page_a_month(string key, string month)
    {
        var page = Page.Render(() => Single());

        await Press(page, key);

        Assert.Contains(">" + month + "<", page.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_grid_keeps_its_keys_from_the_page_and_names_the_tab_stop_for_the_focus_to_follow()
    {
        var html = Single().ToHtml();

        var looked = Single().Static().ToHtml();

        // Flux's list, measured: the arrows, Home, End and the paging keys — not Space, which it lets scroll.
        Assert.Contains("data-rask-contain-keys=\"Arrows Home End PageUp PageDown\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-focus-follows=\"\"", html, StringComparison.Ordinal);
        Assert.Matches("<button[^>]*data-rask-focus-target=\"\"[^>]*aria-label=\"Thursday, January 15, 2026\"", html);
        Assert.Single(Regex.Matches(html, "data-rask-focus-target"));
        Assert.DoesNotContain("data-rask-contain-keys", looked, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-focus", looked, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_arrow_takes_the_focus_target_to_the_new_day_across_a_month_too()
    {
        var page = Page.Render(() => Single(Jan(31)));

        await Press(page, "ArrowRight");
        var html = page.Render();

        Assert.Matches("<button[^>]*data-rask-focus-target=\"\"[^>]*aria-label=\"Sunday, February 1, 2026\"", html);
        Assert.Single(Regex.Matches(html, "data-rask-focus-target"));
    }

    [Theory]
    [InlineData("PageDown")]
    [InlineData("End")]
    [InlineData("PageUp")]
    [InlineData("Home")]
    public async Task A_paging_key_names_no_focus_target_for_its_one_render_so_the_focus_falls_to_the_page(string key)
    {
        var page = Page.Render(() => Single());

        await Press(page, key);
        var paged = page.Html;
        await Press(page, "ArrowRight");

        // Flux leaves the focus on <body> after these four; the tab stop is still there for Tab to find.
        Assert.DoesNotContain("data-rask-focus-target", paged, StringComparison.Ordinal);
        Assert.Single(TabStops(paged));
        Assert.Single(Regex.Matches(page.Html, "data-rask-focus-target"));
    }

    [Fact]
    public void Two_months_that_draw_the_same_day_twice_have_one_tab_stop_in_the_days_own_month()
    {
        var html = Ranged(new UiDateRange(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 3)))
            .OpenTo(new DateOnly(2026, 3, 1)).ForceOpenTo().ToHtml();

        // April 1 closes March's last week as a neighbour's day and stands in April's own first week.
        Assert.Equal(2, Regex.Matches(html, "aria-label=\"Wednesday, April 1, 2026\"").Count);
        Assert.Equal(["Wednesday, April 1, 2026"], TabStops(html));
        Assert.Single(Regex.Matches(html, "data-rask-focus-target"));
    }

    [Fact]
    public async Task The_month_steps_page_the_view_and_leave_the_choice_alone()
    {
        var page = Page.Render(() => Single(Jan(20)));

        await page.On("button[aria-label=\"Next month\"]").Click();
        var html = page.Render();

        Assert.Contains(">February 2026<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-selected=\"true\"", html, StringComparison.Ordinal);
        Assert.Equal(["Sunday, February 1, 2026"], TabStops(html));
    }

    [Fact]
    public async Task The_today_shortcut_comes_back_to_this_month_then_picks_today()
    {
        var page = Page.Render(() => Single().WithToday());

        await page.On("button[aria-label=\"Next month\"]").Click();
        await page.On("button[aria-label=\"Today\"]").Click();
        var back = page.Render();
        await page.On("button[aria-label=\"Today\"]").Click();

        Assert.Contains(">January 2026<", back, StringComparison.Ordinal);
        Assert.DoesNotContain("data-selected", back, StringComparison.Ordinal);
        Assert.Matches("data-date=\"2026-01-15\"[^>]*data-selected", page.Render());
    }

    [Fact]
    public async Task A_selectable_header_offers_the_months_and_a_century_of_years()
    {
        var page = Page.Render(() => Single().SelectableHeader());

        var html = page.Render();
        await page.On("select:has-text(\"Jan\")").Change("6");

        Assert.Equal(2, Regex.Count(html, "<select"));
        Assert.Equal(12 + 111, Regex.Count(html, "<option"));
        Assert.Contains(">1926<", html, StringComparison.Ordinal);
        Assert.Contains(">2036<", html, StringComparison.Ordinal);
        Assert.Contains(">June 2026<", page.Render(), StringComparison.Ordinal);
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
