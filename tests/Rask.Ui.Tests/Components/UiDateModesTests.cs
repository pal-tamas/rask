using System.Globalization;
using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     Several days, a range, and the pickers — all reached through the <c>UiCalendar</c> and <c>UiDatePicker</c>
///     entries, with the model deciding which control it is.
/// </summary>
/// <remarks>
///     Driven through the real handlers with <c>RaskTest</c>: what matters about a range is what the SECOND click
///     writes and that the first writes nothing, and a static render cannot show either. The popover opening and
///     closing is the browser's, and the site's browser suite drives it.
/// </remarks>
public partial class UiDateModesTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly March = new(2026, 3, 1);

    private static string DaySelector(int day) =>
        "button[aria-label=\"" + new DateOnly(2026, 3, day).ToString("D", CultureInfo.CurrentCulture) + "\"]";

    private static DateOnly Mar(int day) => new(2026, 3, day);

    private static int Pressed(string html) => Regex.Matches(html, "aria-pressed=\"true\"").Count;

    // ---- several days ---------------------------------------------------------------------------

    [Fact]
    public void Several_days_open_on_Values_and_mark_each_one()
    {
        var html = UiCalendar.Values([Mar(3), Mar(9)]).Label("Days off").Month(March).ToHtml();

        Assert.Equal(2, Pressed(html));
    }

    [Fact]
    public async Task A_click_adds_a_day_and_a_second_click_takes_it_back_out_in_date_order()
    {
        ICollection<DateOnly>? reported = null;
        var page = RaskTest.Render(() =>
            UiCalendar.Values([Mar(20)]).Label("Days off").Month(March).OnChange(v => { reported = v; }));

        await page.On(DaySelector(5)).ClickAsync();
        Assert.Equal([Mar(5), Mar(20)], reported);

        await page.On(DaySelector(20)).ClickAsync();
        Assert.Equal([], reported);
    }

    [Fact]
    public async Task Binding_a_list_of_days_is_the_several_days_control_and_writes_the_list_back()
    {
        var model = new Holiday();
        var page = RaskTest.Render(() => UiCalendar.Bind(() => model.DaysOff).Label("Days off").Month(March));

        await page.On(DaySelector(12)).ClickAsync();
        await page.On(DaySelector(2)).ClickAsync();

        Assert.Equal([Mar(2), Mar(12)], model.DaysOff);
    }

    // ---- a range --------------------------------------------------------------------------------

    [Fact]
    public void A_range_marks_its_ends_as_chosen_and_tints_the_days_between()
    {
        var html = UiCalendar.Value(new UiDateRange(Mar(10), Mar(13))).Label("Stay").Month(March).ToHtml();

        Assert.Equal(2, Pressed(html));
        Assert.Equal(2, Regex.Matches(html, "btn-square bg-base-200").Count);
    }

    [Fact]
    public async Task The_first_click_writes_nothing_and_the_second_writes_the_whole_range_in_order()
    {
        var calls = 0;
        UiDateRange reported = default;
        var page = RaskTest.Render(() =>
            UiCalendar.Value(default(UiDateRange)).Label("Stay").Month(March)
                .OnChange(r => { calls++; reported = r; }));

        // The later day first: the range still comes out start-to-end.
        await page.On(DaySelector(18)).ClickAsync();
        Assert.Equal(0, calls);
        // The half-picked start is drawn, though, so the reader can see the first click landed.
        Assert.Equal(1, Pressed(page.Html));

        await page.On(DaySelector(11)).ClickAsync();
        Assert.Equal(1, calls);
        Assert.Equal(new UiDateRange(Mar(11), Mar(18)), reported);
    }

    [Fact]
    public async Task A_bound_model_never_holds_half_a_range()
    {
        var model = new Holiday();
        var page = RaskTest.Render(() => UiCalendar.Bind(() => model.Stay).Label("Stay").Month(March));

        await page.On(DaySelector(4)).ClickAsync();
        Assert.Equal(default, model.Stay);

        await page.On(DaySelector(6)).ClickAsync();
        Assert.Equal(new UiDateRange(Mar(4), Mar(6)), model.Stay);
    }

    [Fact]
    public void A_range_knows_what_it_contains_and_is_built_in_either_order()
    {
        var range = UiDateRange.Between(Mar(9), Mar(2));

        Assert.Equal(new UiDateRange(Mar(2), Mar(9)), range);
        Assert.True(range.Contains(Mar(2)));
        Assert.True(range.Contains(Mar(9)));
        Assert.False(range.Contains(Mar(10)));
        Assert.False(default(UiDateRange).Contains(default));
    }

    // ---- the view ------------------------------------------------------------------------------

    [Fact]
    public async Task A_calendar_left_to_itself_pages_between_months()
    {
        // Before, the month steps did nothing without an OnMonth — the arrows were drawn and went nowhere.
        var page = RaskTest.Render(() => UiCalendar.Value(Mar(14)).Label("Delivery date"));
        Assert.Contains(March.ToString("MMMM yyyy", CultureInfo.CurrentCulture), page.Html, StringComparison.Ordinal);

        await page.On("button[aria-label=\"Next month\"]").ClickAsync();

        Assert.Contains(new DateOnly(2026, 4, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture), page.Html,
            StringComparison.Ordinal);
    }

    // ---- the pickers ---------------------------------------------------------------------------

    [Fact]
    public void A_picker_is_a_field_shaped_button_that_opens_a_dialog()
    {
        var html = UiDatePicker.Value(default(DateOnly)).Label("Delivery date").ToHtml();

        Assert.Contains("aria-haspopup=\"dialog\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("popovertarget=\"uidp-", html, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", html, StringComparison.Ordinal);
        Assert.Contains("Choose a date", html, StringComparison.Ordinal);
        // A form field: the visible label names the button through `for`.
        Assert.Matches("<label[^>]*for=\"f-delivery-date\"[^>]*>Delivery date", html);
        Assert.Contains("id=\"f-delivery-date\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_picker_shows_the_chosen_day_in_the_readers_short_format() =>
        Assert.Contains(Mar(14).ToString("d", CultureInfo.CurrentCulture),
            UiDatePicker.Value(Mar(14)).Label("Delivery date").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_single_pick_closes_the_popover_on_the_same_click()
    {
        var html = UiDatePicker.Value(Mar(14)).Label("Delivery date").ToHtml();

        // Every selectable day closes it; the month steps do not.
        Assert.Equal(31, Regex.Matches(html, "popovertargetaction=\"hide\"").Count);
    }

    [Fact]
    public async Task A_pick_in_the_picker_is_committed()
    {
        var model = new Holiday();
        var page = RaskTest.Render(() => UiDatePicker.Bind(() => model.Arrival).Label("Arrival"));
        await page.On("[popover]").RaiseAsync("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");

        var month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        await page.On("button[aria-label=\"" + month.ToString("D", CultureInfo.CurrentCulture) + "\"]").ClickAsync();

        Assert.Equal(month, model.Arrival);
    }

    [Fact]
    public void Several_days_keep_the_popover_open_and_summarise_a_long_choice()
    {
        var html = UiDatePicker.Values([Mar(1), Mar(2), Mar(3), Mar(4), Mar(5)]).Label("Days off").ToHtml();

        Assert.DoesNotContain("popovertargetaction", html, StringComparison.Ordinal);
        Assert.Contains(", +3", System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_range_picker_closes_only_on_the_click_that_gives_the_range_its_end()
    {
        UiDateRange reported = default;
        var page = RaskTest.Render(() =>
            UiDatePicker.Value(new UiDateRange(Mar(2), Mar(4))).Label("Stay").OnChange(r => { reported = r; }));
        await page.On("[popover]").RaiseAsync("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");

        Assert.DoesNotContain("popovertargetaction", page.Html, StringComparison.Ordinal);

        await page.On(DaySelector(20)).ClickAsync();
        // Held, not written — and now the next click is the one that finishes it, so that one closes the popover.
        Assert.Equal(default, reported);
        Assert.Contains("popovertargetaction=\"hide\"", page.Html, StringComparison.Ordinal);

        await page.On(DaySelector(22)).ClickAsync();
        Assert.Equal(new UiDateRange(Mar(20), Mar(22)), reported);
    }

    [Fact]
    public async Task A_controlled_picker_shows_the_day_its_parent_hands_back()
    {
        // The loop a page actually runs: the pick reaches OnChange, the parent stores it, re-renders, and the field
        // has to show it — not the placeholder it started with.
        var page = RaskTest.Render(new PickerHost());
        await page.On("[popover]").RaiseAsync("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");

        var month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        await page.On("button[aria-label=\"" + month.ToString("D", CultureInfo.CurrentCulture) + "\"]").ClickAsync();

        Assert.Contains(month.ToString("d", CultureInfo.CurrentCulture), page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Choose a date", page.Html, StringComparison.Ordinal);
    }

    private sealed partial class PickerHost : global::Rask.Core.Component
    {
        private DateOnly _arrival;

        protected override global::Rask.Core.Component? Render() =>
            UiDatePicker.Value(_arrival).Label("Arrival").OnChange(d => { _arrival = d; });
    }

    private sealed class Holiday
    {
        public List<DateOnly> DaysOff { get; set; } = [];

        public UiDateRange Stay { get; set; }

        public DateOnly Arrival { get; set; }
    }
}
