using System.Globalization;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     The month grid, built in C# because the thing daisyUI styles is a JavaScript web component.
/// </summary>
public partial class UiCalendarTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly March = new(2026, 3, 1);

    [Fact]
    public void It_lays_out_every_day_of_the_month_and_no_more()
    {
        // March 2026 has 31 days. A grid that renders 30 or 32 is a grid whose lead-in arithmetic is
        // wrong, which is invisible in any month that happens to start on the first column.
        //
        // Counted by aria-pressed, which only the DAYS carry: counting <button> catches the two
        // month-step controls as well and makes every expected number two too high.
        var html = Calendar(March);

        Assert.Equal(31, Occurrences(html, "aria-pressed"));
    }

    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2024, 2, 29)]
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 12, 31)]
    public void Every_month_length_is_handled_including_a_leap_February(int year, int month, int days) =>
        Assert.Equal(days, Occurrences(Calendar(new DateOnly(year, month, 1)), "aria-pressed"));

    [Fact]
    public void The_days_are_buttons_in_a_table_so_a_keyboard_reaches_them()
    {
        var html = Calendar(March);

        Assert.Contains("<table", html);
        Assert.Contains("<th", html);
        Assert.Contains("<button", html);
    }

    [Fact]
    public void A_day_carries_its_whole_date_as_its_name()
    {
        // "14" on its own is not something you can act on once the month has scrolled out of earshot.
        var expected = new DateOnly(2026, 3, 14).ToString("D", CultureInfo.CurrentCulture);

        Assert.Contains($"aria-label=\"{expected}\"", Calendar(March));
    }

    [Fact]
    public void The_selected_day_is_marked_for_CSS_and_for_a_screen_reader()
    {
        var html = UiCalendar.Label("Delivery date").Month(March).Selected(new DateOnly(2026, 3, 14))
            .ToHtml();

        Assert.Contains("btn-active", html);
        Assert.Contains("aria-pressed=\"true\"", html);
    }

    [Fact]
    public void Days_outside_the_allowed_range_are_disabled_rather_than_hidden()
    {
        // Hiding them would change the shape of the grid and leave a reader wondering whether the month
        // is short or the date is unavailable.
        var html = UiCalendar.Label("Delivery date").Month(March).Min(new DateOnly(2026, 3, 10))
            .ToHtml();

        Assert.Equal(31, Occurrences(html, "aria-pressed"));
        Assert.Equal(9, Occurrences(html, "disabled"));
    }

    [Fact]
    public void It_offers_a_way_to_each_neighbouring_month()
    {
        var html = Calendar(March);

        Assert.Contains("aria-label=\"Previous month\"", html);
        Assert.Contains("aria-label=\"Next month\"", html);
    }

    [Fact]
    public void The_month_being_shown_is_named_in_words() =>
        Assert.Contains(March.ToString("MMMM yyyy", CultureInfo.CurrentCulture), Calendar(March));

    [Fact]
    public void The_week_starts_where_it_is_told_to()
    {
        // The lead-in blanks depend on it, so getting this wrong shifts every day by a column.
        var monday = UiCalendar.Label("d").Month(March).FirstDay(DayOfWeek.Monday).ToHtml();
        var sunday = UiCalendar.Label("d").Month(March).FirstDay(DayOfWeek.Sunday).ToHtml();

        Assert.NotEqual(monday, sunday);
    }

    [Fact]
    public void It_names_itself_as_a_group() =>
        Assert.Contains("aria-label=\"Delivery date\"", Calendar(March));

    [Fact]
    public void It_ships_no_web_component()
    {
        // daisyUI styles the third-party `cally` element for this, which is JavaScript the kit does not
        // ship. The grid is C#, so nothing here reaches for one.
        Assert.DoesNotContain("calendar-date", Calendar(March));
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    private static string Calendar(DateOnly month) =>
        UiCalendar.Label("Delivery date").Month(month).ToHtml();
}
