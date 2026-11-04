namespace Rask;

/// <summary>A range chosen.</summary>
internal sealed class UiCalendarRangePick(UiDateRange chosen) : UiCalendarPicks
{
    internal override DateOnly? First => chosen == default ? null : chosen.Start;

    internal override bool Range => true;

    internal override string Mode => "range";

    internal override IReadOnlyList<DateOnly?> Typed => chosen == default ? [null, null] : [chosen.Start, chosen.End];

    internal override UiCalendarMarks Marks(DateOnly day) =>
        chosen == default
            ? default
            : new(day == chosen.Start || day == chosen.End, chosen.Contains(day), day == chosen.Start, day == chosen.End, false);

    private protected override IEnumerable<DateOnly> Days => chosen == default ? [] : [chosen.Start, chosen.End];

    // A preset names its range, which the page wrote for today and the reader picks whole, limits or none: what a
    // range says it is must be what that preset spans today. A range picked by hand is ruled by its ends.
    internal override bool DrawnBy(UiCalendarOptions options)
    {
        if (chosen.Preset is not { } preset || preset == Ui.DateRangePreset.Custom)
        {
            return base.DrawnBy(options);
        }

        var spans = UiDateRange.Of(preset, options.Today, options.StartDay, options.Min);
        return spans.Start == chosen.Start && spans.End == chosen.End;
    }
}
