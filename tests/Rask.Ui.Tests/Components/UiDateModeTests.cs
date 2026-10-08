using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux UI's <c>mode</c> on <c>Ui.Calendar</c> and <c>Ui.DatePicker</c>: the step that opens the control picks
///     the one that binds that mode's type, and a mode stated as a value has to agree with what is bound.
/// </summary>
public partial class UiDateModeTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static DateOnly Jan(int day) => new(2026, 1, day);

    [Fact]
    public void A_calendar_with_no_mode_step_picks_a_single_day()
    {
        UiCalendar calendar = Ui.Calendar.Value(Jan(20)).Locale("en-US").On(Today);

        var html = calendar.ToHtml();

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "aria-selected=\"true\""));
        Assert.Same(calendar, calendar.Single);
    }

    [Fact]
    public void The_single_step_is_the_calendar_itself()
    {
        UiCalendar stepped = Ui.Calendar.Single.Value(Jan(20)).Locale("en-US").On(Today);

        var plain = Ui.Calendar.Value(Jan(20)).Locale("en-US").On(Today);

        Assert.Equal(WithoutRefs(plain.ToHtml()), WithoutRefs(stepped.ToHtml()));
    }

    [Fact]
    public void The_multiple_step_opens_a_calendar_over_a_collection_of_days()
    {
        UiCalendarMultiple calendar = Ui.Calendar.Multiple.Values([Jan(5), Jan(9)]).Locale("en-US").On(Today);

        var html = calendar.ToHtml();

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "data-selected").Count);
    }

    [Fact]
    public void The_range_step_opens_a_calendar_over_a_range_and_two_months()
    {
        UiCalendarRange calendar = Ui.Calendar.Range.Value(new UiDateRange(Jan(5), Jan(9))).Locale("en-US").On(Today);

        var html = calendar.ToHtml();

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "role=\"grid\"").Count);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(html, "data-in-range").Count);
    }

    [Fact]
    public void A_mode_given_as_a_value_opens_the_calendar_that_mode_binds()
    {
        var model = new Trip { Stay = new UiDateRange(Jan(5), Jan(9)), DaysOff = [Jan(12)] };

        var range = Ui.Calendar.Mode(Ui.CalendarMode.Range).Bind(() => model.Stay).Locale("en-US").On(Today).ToHtml();
        var several = Ui.Calendar.Mode(Ui.CalendarMode.Multiple).Values(model.DaysOff).Locale("en-US").On(Today).ToHtml();

        Assert.Contains("data-start", range, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(several, "data-selected"));
    }

    [Fact]
    public void A_calendar_whose_mode_disagrees_with_what_it_binds_says_so_when_it_renders()
    {
        var calendar = Ui.Calendar.Mode(Ui.CalendarMode.Range).Value(Jan(20)).Locale("en-US").On(Today);

        var error = Assert.Throws<InvalidOperationException>(() => calendar.ToHtml());

        Assert.Equal(
            "Ui.Calendar is in Range mode but what it binds is Single's (a DateOnly). "
            + "Open it with Ui.Calendar.Range and bind a UiDateRange.",
            error.Message);
    }

    [Fact]
    public void Flux_bare_multiple_agrees_with_a_collection_and_rejects_a_single_day()
    {
        var several = Ui.Calendar.Multiple.Values([Jan(5)]).Multiple().Locale("en-US").On(Today);
        var single = Ui.Calendar.Value(Jan(5)).Multiple().Locale("en-US").On(Today);

        var html = several.ToHtml();
        var error = Assert.Throws<InvalidOperationException>(() => single.ToHtml());

        Assert.Contains("data-selected", html, StringComparison.Ordinal);
        Assert.StartsWith("Ui.Calendar is in Multiple mode", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_date_picker_with_no_mode_step_picks_a_single_day_and_the_range_step_a_range()
    {
        UiDatePicker single = Ui.DatePicker.Value(Jan(20)).Locale("en-US").On(Today);
        UiDatePickerRange range = Ui.DatePicker.Range.Value(new UiDateRange(Jan(5), Jan(9))).Locale("en-US").On(Today);

        var (one, two) = (single.ToHtml(), range.ToHtml());

        Assert.Contains("Jan 20, 2026", one, StringComparison.Ordinal);
        Assert.Contains("Jan 5, 2026", two, StringComparison.Ordinal);
        Assert.Contains("Jan 9, 2026", two, StringComparison.Ordinal);
    }

    [Fact]
    public void A_date_picker_whose_mode_disagrees_with_what_it_binds_says_so_when_it_renders()
    {
        var picker = Ui.DatePicker.Mode(Ui.DatePickerMode.Single).Value(new UiDateRange(Jan(5), Jan(9))).Locale("en-US").On(Today);

        var error = Assert.Throws<InvalidOperationException>(() => picker.ToHtml());

        Assert.Equal(
            "Ui.DatePicker is in Single mode but what it binds is Range's (a UiDateRange). "
            + "Open it with Ui.DatePicker.Single and bind a DateOnly.",
            error.Message);
    }

    [Fact]
    public void An_open_date_picker_keeps_the_focus_on_the_popup_rather_than_a_control_in_it()
    {
        var picker = Ui.DatePicker.Value(Jan(20)).SelectableHeader().Locale("en-US").On(Today);

        var html = picker.ToHtml();

        Assert.Matches("<dialog[^>]*\\bautofocus\\b", html);
        Assert.DoesNotMatch("<(button|select)[^>]*\\bautofocus\\b", html);
    }

    // An element ref's id is a fresh GUID per instance.
    private static string WithoutRefs(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "data-rask-ref=\"[^\"]+\"", "");

    private sealed class Trip
    {
        public UiDateRange Stay { get; set; }

        public List<DateOnly> DaysOff { get; set; } = [];
    }
}
