using System.Text.RegularExpressions;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

/// <summary>
///     The typed date as Flux's reference draws it beyond its examples: <c>flux:date-picker.input</c>'s
///     <c>variant</c>, and <c>with-inputs</c> on the date picker and the calendar.
/// </summary>
/// <remarks>What is typed and picked in the browser is <c>CalendarInputsTests</c>' (Server E2E).</remarks>
public sealed partial class UiDatePickerInputsTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static readonly DateOnly Arrival = new(2026, 1, 20);

    private const string Inputs = "data-rask-calendar-part=\"inputs\"";

    [Fact]
    public void A_typed_field_is_drawn_as_parts_unless_it_asks_for_the_browsers_own()
    {
        var picker = Ui.DatePicker.Value(Arrival).Locale("en-US").On(Today);

        var custom = picker.Trigger(Ui.DatePickerInput).ToHtml();
        var native = Ui.DatePicker.Value(Arrival).Locale("en-US").On(Today).Trigger(Ui.DatePickerInput.Native).ToHtml();

        Assert.Contains("data-rask-segments", custom, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"date\"", custom, StringComparison.Ordinal);
        Assert.Matches("<input [^>]*type=\"date\"[^>]*value=\"2026-01-20\"|<input [^>]*value=\"2026-01-20\"[^>]*type=\"date\"", native);
        Assert.Contains("data-rask-date", native, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-segments", native, StringComparison.Ordinal);
    }

    [Fact]
    public void With_inputs_a_picker_draws_one_typed_field_over_its_months_holding_the_chosen_day()
    {
        var html = Ui.DatePicker.Value(Arrival).WithInputs().Locale("en-US").On(Today).ToHtml();

        var inputs = Part(html);

        Assert.Equal(1, Regex.Count(inputs, "data-rask-segments"));
        Assert.Matches("<input [^>]*type=\"hidden\"[^>]*value=\"2026-01-20\"|<input [^>]*value=\"2026-01-20\"[^>]*type=\"hidden\"", inputs);
        Assert.True(html.IndexOf(Inputs, StringComparison.Ordinal) < html.IndexOf("data-rask-calendar-part=\"header\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_range_draws_its_start_and_its_end_and_native_asks_for_the_browsers_fields()
    {
        var range = new UiDateRange(Arrival, Arrival.AddDays(4));

        var html = Ui.DatePicker.Range.Value(range).WithInputs(Ui.DatePickerInputVariant.Native).Locale("en-US").On(Today).ToHtml();

        var inputs = Part(html);
        Assert.Equal(2, Regex.Count(inputs, "type=\"date\""));
        Assert.Contains("2026-01-20", inputs, StringComparison.Ordinal);
        Assert.Contains("2026-01-24", inputs, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_with_inputs_nothing_is_drawn_over_the_months()
    {
        var html = Ui.DatePicker.Value(Arrival).Locale("en-US").On(Today).ToHtml();

        Assert.DoesNotContain(Inputs, html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_calendar_takes_inputs_for_a_day_or_a_range_and_none_for_several_days()
    {
        var single = Ui.Calendar.Value(Arrival).WithInputs().Locale("en-US").On(Today).ToHtml();
        var multiple = Ui.Calendar.Multiple.Values([Arrival]).WithInputs().Locale("en-US").On(Today).ToHtml();

        Assert.Equal(1, Regex.Count(Part(single), "data-rask-segments"));
        Assert.DoesNotContain(Inputs, multiple, StringComparison.Ordinal);
    }

    // The inputs' host, up to the header that follows it.
    private static string Part(string html)
    {
        var start = html.IndexOf(Inputs, StringComparison.Ordinal);
        Assert.True(start >= 0, html);
        return html[start..html.IndexOf("data-rask-calendar-part=\"header\"", start, StringComparison.Ordinal)];
    }
}
