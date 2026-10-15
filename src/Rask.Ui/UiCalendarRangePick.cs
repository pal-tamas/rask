namespace Rask;

/// <summary>A range chosen.</summary>
internal sealed class UiCalendarRangePick(UiDateRange chosen) : UiCalendarPicks
{
    internal override DateOnly? First => chosen == default ? null : chosen.Start;

    internal override bool Range => true;

    internal override string Mode => "range";

    internal override UiCalendarMarks Marks(DateOnly day) =>
        chosen == default
            ? default
            : new(day == chosen.Start || day == chosen.End, chosen.Contains(day), day == chosen.Start, day == chosen.End, false);
}
