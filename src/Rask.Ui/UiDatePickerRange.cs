namespace Rask;

/// <summary>
/// A field that shows a range of dates and opens two months to pick one — Flux UI's
/// <c>flux:date-picker mode="range"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Opened by Flux's mode step on the picker's entry: <c>Ui.DatePicker.Range.Bind(() =&gt; model.Stay)</c>
/// over a <see cref="UiDateRange" /> (or a <c>UiDateRange?</c>). The first click is the start, the second writes
/// the range and closes the popup.
/// </para>
/// <para>
/// <c>WithPresets()</c> lists Flux's frequently used ranges beside the months, and <c>Presets([...])</c> says
/// which. A preset writes its range with <see cref="UiDateRange.Preset" /> set, and the button then shows its
/// name; a range picked by hand shows its two dates.
/// </para>
/// </remarks>
public sealed partial class UiDatePickerRange : UiDatePickerControl<UiDateRange>
{
    private protected override Ui.DatePickerMode Bound => Ui.DatePickerMode.Range;

    private protected override int DefaultMonths => 2;

    private protected override string DefaultPlaceholder => RaskStrings.Get(RaskString.DatePickerRangePlaceholder, "Select a date range");

    private protected override string ConfirmLabel => RaskStrings.Get(RaskString.DatePickerConfirmRange, "Select range");

    private protected override UiCalendarPicks Picks(UiDateRange current) => new UiCalendarRangePick(current);

    private protected override string Written(UiDateRange value) => UiCalendarValue.Range(value);

    private protected override bool TryRead(string text, out UiDateRange value) => UiCalendarValue.TryRange(text, out value);

    private protected override string? Text(UiDateRange current, UiCalendarOptions options)
    {
        if (current == default)
        {
            return null;
        }

        return current.Preset is { } preset && preset != Ui.DateRangePreset.Custom
            ? UiDateRangePresets.Label(preset)
            : UiDatePickerText.Medium(current.Start, options.Culture) + UiDatePickerText.Separator + UiDatePickerText.Medium(current.End, options.Culture);
    }

    private protected override IReadOnlyList<DateOnly?> Dates(UiDateRange current) =>
        current == default ? [null, null] : [current.Start, current.End];

    // A typed start keeps the end where it can; a typed end before the start begins the range there.
    private protected override UiDateRange Typed(UiDateRange current, int slot, DateOnly date)
    {
        if (current == default)
        {
            return new UiDateRange(date, date);
        }

        return slot == 0 ? UiDateRange.Between(date, current.End) : UiDateRange.Between(current.Start, date);
    }

    private protected override Component? Aside(UiDateRange current, UiCalendarOptions options) =>
        WithPresets != true && Presets is null ? null : UiDatePickerPresets.Render(Presets ?? UiDateRangePresets.Default, current, options);

    private protected override Component? Below(UiDateRange current, UiCalendarOptions options) =>
        WithPresets != true && Presets is null ? null : UiDatePickerPresets.Narrow(Presets ?? UiDateRangePresets.Default, current, options);
}
