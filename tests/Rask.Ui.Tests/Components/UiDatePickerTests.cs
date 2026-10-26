using System.Text.RegularExpressions;
using Rask.Testing;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.DatePicker</c> — Flux UI's date picker — for a day and for a range: what the trigger shows, what a
///     pick writes and when, and the markup a reader's keyboard and screen reader depend on.
/// </summary>
/// <remarks>
///     The page's first paint and the value it hears. The popup opening, closing, what a pick does in it and where
///     it sits are the browser's (<c>rask-calendar.ts</c>, <c>RuntimeHookCalendarTests</c>);
///     <c>scripts/flux/parity-date.mjs</c> holds those to Flux's live page.
/// </remarks>
public partial class UiDatePickerTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static string Day(int day) => "button[aria-label=\"" + Jan(day).ToString("D", System.Globalization.CultureInfo.GetCultureInfo("en-US")) + "\"]";

    // The one field the page hears the choice through, first in the picker.
    private const string Field = "[data-ui-date-picker] > input[type=\"hidden\"]";

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
    public async Task The_day_the_browser_announces_is_written_with_the_next_action_and_shown_on_the_button()
    {
        var model = new Trip();
        var page = Acting.Render(() => Ui.DatePicker.Bind(() => model.Arrival).Locale("en-US").On(Today));

        var waits = page.Find(Field).Attribute("data-rask-bind-on");
        await page.On(Field).Change("2026-01-20");
        await Acting.Act(page);

        Assert.Equal("action", waits);
        Assert.Equal(Jan(20), model.Arrival);
        Assert.Contains(">Jan 20, 2026</span>", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("popovertargetaction", page.Html, StringComparison.Ordinal);
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
    public async Task A_range_is_written_whole_and_never_half()
    {
        var model = new Trip();
        var page = Acting.Render(() => Ui.DatePicker.Range.Bind(() => model.Stay).Locale("en-US").On(Today));

        await page.On(Field).Change("2026-01-04");
        await Acting.Act(page);
        var half = model.Stay;
        await page.On(Field).Change("2026-01-04/2026-01-06");
        await Acting.Act(page);

        Assert.Equal(default, half);
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
    public void The_presets_rove_and_the_tab_stop_is_the_first_until_a_preset_is_checked()
    {
        var unchosen = Ui.DatePicker.Range.Value(default(UiDateRange)).WithPresets().Min(new DateOnly(2012, 1, 1)).Locale("en-US").On(Today).ToHtml();
        var chosen = Ui.DatePicker.Range.Value(UiDateRange.Of(Ui.DateRangePreset.Last7Days, Today)).WithPresets().Locale("en-US").On(Today).ToHtml();

        // As Flux marks them: `data-active` and tabindex 0 on the first while Custom is the checked one.
        Assert.Contains("data-rask-roving=\"\" data-rask-calendar-part=\"presets\" role=\"radiogroup\"", unchosen, StringComparison.Ordinal);
        Assert.Matches("<button[^>]*data-active=\"\"[^>]*tabindex=\"0\"[^>]*value=\"today\"", unchosen);
        Assert.Matches("<button[^>]*tabindex=\"-1\"[^>]*value=\"custom\"", unchosen);
        Assert.Matches("<button[^>]*data-active=\"\" data-checked=\"\"[^>]*tabindex=\"0\"[^>]*value=\"last7Days\" data-rask-calendar-range=\"2026-01-09/2026-01-15\"", chosen);
        Assert.Single(Regex.Matches(chosen, "role=\"radio\" tabindex=\"0\"|tabindex=\"0\"[^>]*role=\"radio\""));
    }

    [Fact]
    public void The_closed_button_keeps_its_arrows_and_the_popup_holds_the_page_still()
    {
        var html = Single(Jan(20)).ToHtml();

        Assert.Contains("data-rask-contain-keys=\"ArrowUp ArrowDown\"", html, StringComparison.Ordinal);
        Assert.Matches("<dialog[^>]*data-rask-lock=\"\"", html);
        Assert.DoesNotContain("data-rask-popover-open", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_preset_writes_its_range_and_puts_its_name_on_the_button()
    {
        var model = new Trip();
        var page = Acting.Render(() => Ui.DatePicker.Range.Bind(() => model.Stay).Presets([Ui.DateRangePreset.Last7Days]).Locale("en-US").On(Today));

        await page.On(Field).Change("2026-01-09/2026-01-15/last7Days");
        await Acting.Act(page);

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
    public void With_confirmation_the_button_that_takes_the_choice_is_the_runtimes()
    {
        var html = Ui.DatePicker.Value(default(DateOnly)).WithConfirmation().Locale("en-US").On(Today).ToHtml();

        Assert.Matches("<div class=\"inline max-sm:block\" role=\"button\" tabindex=\"0\" data-rask-calendar-part=\"confirm\">", html);
        Assert.Contains("&quot;confirms&quot;:true", html, StringComparison.Ordinal);
        Assert.DoesNotContain("popovertargetaction", html, StringComparison.Ordinal);
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
        var page = Acting.Render(() => Ui.DatePicker.Bind(() => model.Arrival).Type(Ui.DatePickerType.Input).Locale("en-US").On(Today));

        await page.On("[data-ui-date-inputs] input[type=\"hidden\"]").Change(string.Empty);
        var early = model.Arrival;
        await page.On("[data-ui-date-inputs] input[type=\"hidden\"]").Change("2026-03-31");
        await Acting.Act(page);

        Assert.Equal(default, early);
        Assert.Equal(new DateOnly(2026, 3, 31), model.Arrival);
    }

    [Fact]
    public async Task Two_typed_fields_in_the_trigger_slot_are_a_ranges_start_and_end()
    {
        var model = new Trip { Stay = new UiDateRange(Jan(4), Jan(6)) };
        var page = Acting.Render(() => Ui.DatePicker.Range.Bind(() => model.Stay).Locale("en-US").On(Today)
            .Trigger(Div[Ui.DatePickerInput.Label("Start"), Ui.DatePickerInput.Label("End")]));

        var days = Regex.Matches(page.Html, "value=\"(2026-\\d\\d-\\d\\d)\"").Select(m => m.Groups[1].Value).ToArray();
        await page.On("#f-end input[type=\"hidden\"]").Change("2026-01-09");
        await Acting.Act(page);

        Assert.Equal(["2026-01-04", "2026-01-06"], days);
        Assert.Equal(new UiDateRange(Jan(4), Jan(9)), model.Stay);
    }

    [Fact]
    public void A_clearable_picker_draws_its_cross_out_of_sight_until_a_day_is_chosen()
    {
        var plain = Single(Jan(20)).ToHtml();

        var chosen = Single(Jan(20)).Clearable().ToHtml();
        var unset = Single(default).Clearable().ToHtml();
        var disabled = Single(Jan(20)).Clearable().Disabled().ToHtml();

        Assert.DoesNotContain("aria-label=\"Clear\"", plain, StringComparison.Ordinal);
        Assert.StartsWith("<div class=\"relative flex items-center", Cross(chosen), StringComparison.Ordinal);
        Assert.StartsWith("<div class=\"relative hidden items-center", Cross(unset), StringComparison.Ordinal);
        Assert.StartsWith("<div class=\"relative hidden items-center", Cross(disabled), StringComparison.Ordinal);
        Assert.Contains("data-rask-calendar-part=\"clear\"", Cross(chosen), StringComparison.Ordinal);
    }

    [Fact]
    public void The_cross_is_Fluxs_a_div_in_the_button_before_the_chevron_that_tab_passes_by()
    {
        var picker = Single(Jan(20)).Clearable();

        var html = picker.ToHtml();

        var cross = Cross(html);
        Assert.Contains("size-8 ms-2 -me-2 rounded-md cursor-pointer", cross, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"-1\"", cross, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Clear\"", cross, StringComparison.Ordinal);
        Assert.True(BeforeChevron(html, "data-ui-date-picker-button"), "the × is not in the button, before its chevron.");
    }

    [Fact]
    public void The_cross_of_a_typed_field_stands_in_the_field_before_its_chevron()
    {
        var picker = Single(Jan(20)).Type(Ui.DatePickerType.Input).Clearable();

        var html = picker.ToHtml();

        Assert.True(BeforeChevron(html, "data-rask-toggle"), "the × is not in the typed field, before its chevron.");
        Assert.DoesNotContain("end-9", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trigger_in_the_slot_draws_the_cross_itself()
    {
        var picker = Single(Jan(20)).Trigger(Ui.DatePickerButton.Size(Ui.DatePickerButtonSize.Sm).Clearable());

        var html = picker.ToHtml();

        Assert.True(BeforeChevron(html, "data-ui-date-picker-button"), "the × is not in the button, before its chevron.");
        Assert.Contains("aria-label=\"Clear\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_cross_takes_a_day_that_may_be_unset_back_to_null()
    {
        var model = new Trip { Departure = Jan(20) };
        var page = Acting.Render(() => Ui.DatePicker.Bind(() => model.Departure).Clearable().Locale("en-US").On(Today));

        await page.On("[aria-label=\"Clear\"]").Click();
        await Acting.Act(page);

        Assert.Null(model.Departure);
        Assert.Contains(">Select a date</span>", page.Html, StringComparison.Ordinal);
        Assert.StartsWith("<div class=\"relative hidden items-center", Cross(page.Html), StringComparison.Ordinal);
    }

    // The ×'s opening tag, as a picker or a select draws it in its trigger.
    internal static string Cross(string html) =>
        Regex.Match(html, "<div class=\"relative (?:flex|hidden) items-center justify-center gap-2 size-8[^>]*>").Value;

    // Whether the × stands in the trigger marked so, right before the trigger's chevron.
    internal static bool BeforeChevron(string html, string trigger) =>
        Regex.IsMatch(
            html,
            trigger + "[^>]*>.*?<div class=\"relative (?:flex|hidden) items-center justify-center gap-2 size-8[^>]*><svg[^>]*>(?:(?!</svg>).)*</svg></div><svg[^>]*class=\"[^\"]*ms-2 -me-1",
            RegexOptions.Singleline);

    [Fact]
    public async Task The_clear_button_takes_a_day_that_cannot_be_null_back_to_its_unset_value()
    {
        var model = new Trip { Arrival = Jan(20) };
        var page = Acting.Render(() => Ui.DatePicker.Bind(() => model.Arrival).Clearable().Locale("en-US").On(Today));

        await page.On("[aria-label=\"Clear\"]").Click();
        await Acting.Act(page);

        Assert.Equal(default, model.Arrival);
        Assert.Contains(">Select a date</span>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_clear_button_hands_the_parent_that_owns_the_day_an_unset_one()
    {
        var held = Jan(20);
        var page = Page.Render(() => Single(held).OnChange(day => held = day).Clearable());

        await page.On("[aria-label=\"Clear\"]").Click();

        Assert.Equal(default, held);
        Assert.Contains(">Select a date</span>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_clear_button_empties_a_whole_range_and_both_of_its_typed_fields()
    {
        var model = new Trip { Stay = new UiDateRange(Jan(4), Jan(6)) };
        var page = Acting.Render(() => Ui.DatePicker.Range.Bind(() => model.Stay).Locale("en-US").On(Today)
            .Trigger(Div[Ui.DatePickerInput.Label("Start"), Ui.DatePickerInput.Label("End").Clearable()]));

        await page.On("[aria-label=\"Clear\"]").Click();
        await Acting.Act(page);

        Assert.Equal(default, model.Stay);
        Assert.Equal(3, Regex.Matches(page.Html, "<input[^>]*type=\"hidden\"[^>]*value=\"\"|<input[^>]*value=\"\"[^>]*type=\"hidden\"").Count);
        Assert.DoesNotContain("value=\"2026-", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_picker_is_never_narrower_than_Fluxs_until_a_class_of_the_call_site_sizes_it()
    {
        var unsized = Single(Jan(4));
        var grown = Single(Jan(4)).Class("flex-1");

        var least = RootClass(unsized.ToHtml(), "date-picker");
        var sized = RootClass(grown.ToHtml(), "date-picker");

        Assert.Equal("min-w-[12rem] sm:min-w-[240px]", least);
        Assert.Equal("[:where(&)]:min-w-0 flex-1", sized);
    }

    internal static string RootClass(string html, string part) =>
        System.Net.WebUtility.HtmlDecode(Regex.Match(html, "<div class=\"([^\"]*)\"[^>]*\\bdata-ui-" + part + "(?=[ >=])").Groups[1].Value);

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
