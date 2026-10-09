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

    private protected override string ConfirmLabel => RaskStrings.Get(RaskString.DatePickerConfirm, "Select date");

    private protected override UiCalendarPicks Picks(UiDateRange current, Func<UiDateRange, Task> choose) =>
        new UiCalendarRangePick(current, State, MinRange, MaxRange, choose);

    private protected override string? Text(UiDateRange current, UiCalendarOptions options)
    {
        if (current == default)
        {
            return null;
        }

        return current.Preset is { } preset && preset != Ui.DateRangePreset.Custom
            ? UiDateRangePresets.Label(preset)
            : Medium(current.Start, options.Culture) + " – " + Medium(current.End, options.Culture);
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

    private protected override Component? Aside(UiDateRange current, UiCalendarOptions options, Func<UiDateRange, Task> choose)
    {
        if (WithPresets != true && Presets is null)
        {
            return null;
        }

        // A choice waiting to be confirmed keeps the popup open; otherwise a preset closes it as a pick does.
        var closes = WithConfirmation == true ? null : PopoverId;
        return UiDatePickerPresets.Render(Presets ?? UiDateRangePresets.Default, current, options, closes, choose);
    }
}
