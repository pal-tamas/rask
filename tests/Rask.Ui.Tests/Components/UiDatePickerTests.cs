using System.Text.RegularExpressions;
using Rask.Testing;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.DatePicker</c> — Flux UI's date picker — for a day and for a range: what the trigger shows, what a
///     pick writes and when, and the markup a reader's keyboard and screen reader depend on.
/// </summary>
/// <remarks>
///     Driven through the real handlers with <c>Page</c>. The popup opening, closing and where it sits are the
///     browser's; <c>scripts/flux/parity-date.mjs</c> holds those to Flux's live page.
/// </remarks>
public partial class UiDatePickerTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static string Day(int day) => "button[aria-label=\"" + Jan(day).ToString("D", System.Globalization.CultureInfo.GetCultureInfo("en-US")) + "\"]";

    private static UiDatePicker Single(DateOnly value) => Ui.DatePicker.Value(value).Locale("en-US").On(Today);

    private static UiDatePickerRange Ranged(UiDateRange value) => Ui.DatePicker.Range.Value(value).Locale("en-US").On(Today);

    [Fact]
    public void A_picker_shows_its_placeholder_until_a_day_is_chosen()
    {
        var unset = Single(default).ToHtml();

        var custom = Single(default).Placeholder("When?").ToHtml();

        Assert.Contains(">Select a date</span>", unset, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Select a date\"", unset, StringComparison.Ordinal);
        Assert.Contains(">When?</span>", custom, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chosen_day_is_shown_as_the_locale_writes_a_medium_date()
    {
        var picker = Single(Jan(20));

        var html = picker.ToHtml();

        Assert.Contains(">Jan 20, 2026</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-date-picker-placeholder", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_trigger_is_a_combobox_that_opens_the_popup_holding_the_calendar()
    {
        var html = Single(default).ToHtml();

        var popup = Regex.Match(html, "popovertarget=\"([^\"]+)\"").Groups[1].Value;

        Assert.Contains("role=\"combobox\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-haspopup=\"listbox\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"" + popup + "-calendar\"", html, StringComparison.Ordinal);
        Assert.Contains("<dialog id=\"" + popup + "\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"grid\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_trigger_says_it_is_expanded_while_the_browser_has_the_popup_open()
    {
        var page = Page.Render(() => Single(default));

        await page.On("[popover]").Raise("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");

        Assert.Contains("aria-expanded=\"true\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pick_writes_the_bound_day_and_closes_the_popup_on_the_same_click()
    {
        var model = new Trip();
        var page = Page.Render(() => Ui.DatePicker.Bind(() => model.Arrival).Locale("en-US").On(Today));

        Assert.Contains("popovertargetaction=\"hide\"", page.Html, StringComparison.Ordinal);
        await page.On(Day(20)).Click();

        Assert.Equal(Jan(20), model.Arrival);
        Assert.Contains(">Jan 20, 2026</span>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_picker_bound_to_a_nullable_day_shows_the_placeholder_for_null()
    {
        var model = new Trip { Departure = Jan(9) };

        var set = Ui.DatePicker.Bind(() => model.Departure).Locale("en-US").On(Today).ToHtml();
        var unset = Ui.DatePicker.Bind(() => model.Return).Locale("en-US").On(Today).ToHtml();

        Assert.Contains(">Jan 9, 2026</span>", set, StringComparison.Ordinal);
        Assert.Contains(">Select a date</span>", unset, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_picker_nobody_answers_keeps_the_day_it_was_given()
    {
        var page = Page.Render(() => Single(default));

        await page.On(Day(8)).Click();

        Assert.Contains(">Jan 8, 2026</span>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_range_is_written_on_the_second_click_and_never_half()
    {
        var model = new Trip();
        var page = Page.Render(() => Ui.DatePicker.Range.Bind(() => model.Stay).Locale("en-US").On(Today));

        await page.On(Day(4)).Click();
        var afterFirst = model.Stay;
        await page.On(Day(6)).Click();

        Assert.Equal(default, afterFirst);
        Assert.Equal(new UiDateRange(Jan(4), Jan(6)), model.Stay);
        Assert.Matches(">Jan 4, 2026 [^<]+ Jan 6, 2026</span>", page.Html);
    }

    [Fact]
    public void A_range_picker_shows_two_months_and_its_own_placeholder()
    {
        var picker = Ranged(default);

        var html = picker.ToHtml();

        Assert.Equal(2, Regex.Matches(html, "role=\"grid\"").Count);
        Assert.Contains(">Select a date range</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Range_limits_rule_out_the_days_that_would_break_them_once_a_start_waits()
    {
        var state = new UiCalendarState { Anchor = Jan(10) };

        var picks = new UiCalendarRangePick(default, state, 3, 5, _ => Task.CompletedTask);

        Assert.True(picks.Blocks(Jan(9)));
        Assert.True(picks.Blocks(Jan(11)));
        Assert.False(picks.Blocks(Jan(12)));
        Assert.False(picks.Blocks(Jan(14)));
        Assert.True(picks.Blocks(Jan(15)));
    }

    [Fact]
    public void Presets_are_listed_as_radios_with_custom_last_and_checked_while_nothing_else_fits()
    {
        var picker = Ranged(default).WithPresets().Min(new DateOnly(2012, 1, 1));

        var html = picker.ToHtml();

        Assert.Contains("role=\"radiogroup\"", html, StringComparison.Ordinal);
        Assert.Equal(
            ["today", "yesterday", "thisWeek", "last7Days", "thisMonth", "yearToDate", "allTime", "custom"],
            Regex.Matches(html, "role=\"radio\"[^>]*value=\"([^\"]+)\"").Select(m => m.Groups[1].Value));
        Assert.Matches("data-checked[^>]*role=\"radio\"[^>]*aria-checked=\"true\"[^>]*value=\"custom\"", html);
    }

    [Fact]
    public async Task The_presets_rove_and_the_tab_stop_is_the_first_until_a_preset_is_checked()
    {
        var model = new Trip();
        var page = Page.Render(() => Ui.DatePicker.Range.Bind(() => model.Stay).WithPresets().Min(new DateOnly(2012, 1, 1)).Locale("en-US").On(Today));
        var unchosen = page.Html;

        await page.On("[role=\"radio\"][value=\"last7Days\"]").Click();

        // As Flux marks them: `data-active` and tabindex 0 on the first while Custom is the checked one.
        Assert.Contains("data-rask-roving=\"\" role=\"radiogroup\"", unchosen, StringComparison.Ordinal);
        Assert.Matches("<button[^>]*data-active=\"\"[^>]*tabindex=\"0\"[^>]*value=\"today\"", unchosen);
        Assert.Matches("<button[^>]*tabindex=\"-1\"[^>]*value=\"custom\"", unchosen);
        Assert.Matches("<button[^>]*data-active=\"\" data-checked=\"\"[^>]*tabindex=\"0\"[^>]*value=\"last7Days\"", page.Html);
        Assert.Single(Regex.Matches(page.Html, "role=\"radio\" tabindex=\"0\"|tabindex=\"0\"[^>]*role=\"radio\""));
    }

    [Theory]
    [InlineData("ArrowDown")]
    [InlineData("ArrowUp")]
    public async Task An_arrow_on_the_closed_button_opens_the_popup_which_holds_the_page_still(string key)
    {
        var page = Page.Render(() => Single(Jan(20)));
        var closed = page.Html;

        await page.On("[data-ui-date-picker-button]").Raise("keydown", "{\"key\":\"" + key + "\"}");

        Assert.Contains("data-rask-contain-keys=\"ArrowUp ArrowDown\"", closed, StringComparison.Ordinal);
        Assert.Matches("<dialog[^>]*data-rask-popover-open=\"false\" data-rask-lock=\"\"", closed);
        Assert.Matches("<dialog[^>]*data-rask-popover-open=\"true\" data-rask-lock=\"\"", page.Html);
    }

    [Fact]
    public async Task A_preset_writes_its_range_and_puts_its_name_on_the_button()
    {
        var model = new Trip();
        var page = Page.Render(() => Ui.DatePicker.Range.Bind(() => model.Stay).Presets([Ui.DateRangePreset.Last7Days]).Locale("en-US").On(Today));

        await page.On("[role=\"radio\"][value=\"last7Days\"]").Click();

        Assert.Equal(Jan(9), model.Stay.Start);
        Assert.Equal(Jan(15), model.Stay.End);
        Assert.Equal(Ui.DateRangePreset.Last7Days, model.Stay.Preset);
        Assert.Contains(">Last 7 Days</span>", page.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.DateRangePreset.Today, 15, 15)]
    [InlineData(Ui.DateRangePreset.Yesterday, 14, 14)]
    [InlineData(Ui.DateRangePreset.ThisWeek, 11, 17)]
    [InlineData(Ui.DateRangePreset.Last7Days, 9, 15)]
    [InlineData(Ui.DateRangePreset.ThisMonth, 1, 31)]
    [InlineData(Ui.DateRangePreset.YearToDate, 1, 15)]
    public void A_default_preset_is_the_range_Flux_makes_of_it(Ui.DateRangePreset preset, int start, int end)
    {
        var range = UiDateRange.Of(preset, Today);

        var days = (range.Start, range.End);

        Assert.Equal((Jan(start), Jan(end)), days);
        Assert.Equal(preset, range.Preset);
    }

    [Fact]
    public void All_time_runs_from_the_minimum_to_today()
    {
        var min = new DateOnly(2012, 1, 1);

        var range = UiDateRange.Of(Ui.DateRangePreset.AllTime, Today, min: min);

        Assert.Equal((min, Today), (range.Start, range.End));
        Assert.Equal(5129, range.Count);
    }

    [Fact]
    public void Days_outside_the_limits_and_unavailable_ones_are_disabled_in_the_popup()
    {
        var picker = Single(default).Min(Jan(5)).Max(Jan(25)).Unavailable([Jan(12)]);

        var html = picker.ToHtml();

        Assert.Matches("data-date=\"2026-01-04\"[^>]*aria-disabled=\"true\"", html);
        Assert.Matches("data-date=\"2026-01-26\"[^>]*aria-disabled=\"true\"", html);
        Assert.Matches("data-date=\"2026-01-12\"[^>]*data-unavailable[^>]*aria-disabled=\"true\"", html);
        Assert.DoesNotMatch("data-date=\"2026-01-13\"[^>]*aria-disabled", html);
    }

    [Fact]
    public async Task With_confirmation_a_pick_waits_for_the_button()
    {
        var model = new Trip();
        var page = Page.Render(() => Ui.DatePicker.Bind(() => model.Arrival).WithConfirmation().Locale("en-US").On(Today));

        await page.On(Day(20)).Click();
        var held = model.Arrival;
        await page.On("div[role=\"button\"]").Click();

        Assert.Equal(default, held);
        Assert.Equal(Jan(20), model.Arrival);
        Assert.DoesNotContain("popovertargetaction", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_choice_nobody_confirmed_is_gone_when_the_popup_closes()
    {
        var model = new Trip();
        var page = Page.Render(() => Ui.DatePicker.Bind(() => model.Arrival).WithConfirmation().Locale("en-US").On(Today));

        await page.On(Day(20)).Click();
        await page.On("[popover]").Raise("toggle", "{\"oldState\":\"open\",\"newState\":\"closed\"}");
        await page.On("div[role=\"button\"]").Click();

        Assert.Equal(default, model.Arrival);
    }

    [Fact]
    public void A_label_and_a_description_wrap_the_picker_in_a_field()
    {
        var picker = Single(default).Label("Arrival").Description("The day you check in.");

        var html = picker.ToHtml();

        Assert.Contains("data-ui-field", html, StringComparison.Ordinal);
        Assert.Matches("<label id=\"f-arrival-label\"[^>]*for=\"f-arrival\"", html);
        Assert.Contains("aria-describedby=\"f-arrival-description\"", html, StringComparison.Ordinal);
        Assert.Contains("The day you check in.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_or_invalid_picker_says_so_on_its_trigger()
    {
        var disabled = Single(default).Disabled().ToHtml();

        var invalid = Single(default).Invalid().ToHtml();

        Assert.Matches("role=\"combobox\"[^>]*disabled", disabled);
        Assert.Matches("data-invalid[^>]*role=\"combobox\"[^>]*aria-invalid=\"true\"", invalid);
    }

    [Fact]
    public void The_typed_trigger_has_a_field_for_the_month_the_day_and_the_year_in_the_locales_order()
    {
        var american = Single(Jan(20)).Type(Ui.DatePickerType.Input).ToHtml();

        var german = Single(Jan(20)).Locale("de-DE").Type(Ui.DatePickerType.Input).ToHtml();

        Assert.Equal(["Month", "Day", "Year"], Segments(american));
        Assert.Equal(["Day", "Month", "Year"], Segments(german));
        Assert.Contains("role=\"group\"", american, StringComparison.Ordinal);
    }

    [Fact]
    public void The_typed_parts_are_the_runtimes_segments_around_one_hidden_field_that_carries_the_whole_date()
    {
        var html = Single(Jan(20)).Type(Ui.DatePickerType.Input).ToHtml();

        var parts = Regex.Matches(html, "<input[^>]*data-rask-segment=\"(\\w+)\"[^>]*>").Select(m => m.Value).ToArray();

        // A part has no value of its own: the runtime fills it from the hidden field, and owns what is typed.
        Assert.Contains("data-ui-date-inputs=\"\" data-rask-segments=\"\"", html, StringComparison.Ordinal);
        Assert.Equal(3, parts.Length);
        Assert.DoesNotContain(parts, part => part.Contains("value=", StringComparison.Ordinal));
        Assert.Matches("<input[^>]*type=\"hidden\"[^>]*value=\"2026-01-20\"|<input[^>]*value=\"2026-01-20\"[^>]*type=\"hidden\"", html);
    }

    [Fact]
    public void Everything_in_the_typed_trigger_but_a_field_opens_the_calendar_and_a_disabled_one_opens_nothing()
    {
        var html = Single(Jan(20)).Type(Ui.DatePickerType.Input).ToHtml();

        var disabled = Single(Jan(20)).Type(Ui.DatePickerType.Input).Disabled().ToHtml();

        // The whole trigger toggles; the runtime leaves a press in a field inside it to the field.
        Assert.Matches("<div[^>]*data-rask-toggle=\"ui-date-picker-\\d+\"[^>]*role=\"group\"", html);
        Assert.Single(Regex.Matches(html, "data-rask-toggle"));
        Assert.DoesNotContain("data-rask-toggle", disabled, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_typed_date_is_written_when_the_hidden_field_carries_a_whole_one()
    {
        var model = new Trip();
        var page = Page.Render(() => Ui.DatePicker.Bind(() => model.Arrival).Type(Ui.DatePickerType.Input).Locale("en-US").On(Today));

        await page.On("[data-ui-date-inputs] input[type=\"hidden\"]").Change(string.Empty);
        var early = model.Arrival;
        await page.On("[data-ui-date-inputs] input[type=\"hidden\"]").Change("2026-03-31");

        Assert.Equal(default, early);
        Assert.Equal(new DateOnly(2026, 3, 31), model.Arrival);
    }

    [Fact]
    public async Task Two_typed_fields_in_the_trigger_slot_are_a_ranges_start_and_end()
    {
        var model = new Trip { Stay = new UiDateRange(Jan(4), Jan(6)) };
        var page = Page.Render(() => Ui.DatePicker.Range.Bind(() => model.Stay).Locale("en-US").On(Today)
            .Trigger(Div[Ui.DatePickerInput.Label("Start"), Ui.DatePickerInput.Label("End")]));

        var days = Regex.Matches(page.Html, "value=\"(2026-\\d\\d-\\d\\d)\"").Select(m => m.Groups[1].Value).ToArray();
        await page.On("#f-end input[type=\"hidden\"]").Change("2026-01-09");

        Assert.Equal(["2026-01-04", "2026-01-06"], days);
        Assert.Equal(new UiDateRange(Jan(4), Jan(9)), model.Stay);
    }

    private static string[] Segments(string html) =>
        [.. Regex.Matches(html, "<input[^>]*aria-label=\"(Month|Day|Year)\"").Select(m => m.Groups[1].Value)];

    private sealed class Trip
    {
        public DateOnly Arrival { get; set; }

        public DateOnly? Departure { get; set; }

        public DateOnly? Return { get; set; }

        public UiDateRange Stay { get; set; }
    }
}
