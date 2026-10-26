namespace Rask;

/// <summary>
/// A field that shows a start and an end, each with its own time — Flux UI's <c>flux:datetime-picker mode="range"</c>.
/// </summary>
/// <remarks>
/// Opened by Flux's mode step: <c>Ui.DatetimePicker.Range.Bind(() =&gt; model.Window)</c> over a
/// <see cref="UiDateTimeRange" /> (or a <c>UiDateTimeRange?</c>). Two clicks pick the days, the two fields under the
/// months their times, and Select range writes it; a range whose end is not after its start cannot be selected.
/// </remarks>
public sealed partial class UiDatetimePickerRange : UiDatetimePickerControl<UiDateTimeRange>
{
    private protected override Ui.DatetimePickerMode Bound => Ui.DatetimePickerMode.Range;

    private protected override int DefaultMonths => 2;

    private protected override string DefaultPlaceholder => RaskStrings.Get(RaskString.DatetimePickerRangePlaceholder, "Select a date and time range");

    private protected override string ConfirmLabel => RaskStrings.Get(RaskString.DatePickerConfirmRange, "Select range");

    private protected override UiCalendarPicks Picks(UiDateTimeRange current) =>
        new UiCalendarRangePick(current == default ? default : new UiDateRange(DateOnly.FromDateTime(current.Start), DateOnly.FromDateTime(current.End)));

    private protected override (DateOnly? Day, TimeOnly? Time)[] Ends(UiDateTimeRange current) =>
        current == default ? [(null, null), (null, null)] : [End(current.Start), End(current.End)];

    private protected override string Written(UiDateTimeRange value) => UiDatetimePickerValue.Range(value);

    private protected override bool TryRead(string text, out UiDateTimeRange value) => UiDatetimePickerValue.TryRange(text, out value);

    private protected override (string Before, string Between, string After)? Text(UiDateTimeRange current, UiDatetimeTimes times, DateOnly today)
    {
        if (current == default)
        {
            return null;
        }

        var (start, end) = UiDatetimePickerText.Range(current, times.Pattern, times.Culture, today);
        return (start, UiDatetimePickerText.Separator, end);
    }

    private static (DateOnly?, TimeOnly?) End(DateTime moment) => (DateOnly.FromDateTime(moment), new TimeOnly(moment.Hour, moment.Minute));
}
