namespace Rask;

/// <summary>Several days chosen.</summary>
internal sealed class UiCalendarMultiplePick(ICollection<DateOnly> chosen) : UiCalendarPicks
{
    internal override DateOnly? First => chosen.Count > 0 ? chosen.Min() : null;

    internal override string Mode => "multiple";

    internal override UiCalendarMarks Marks(DateOnly day) => new(chosen.Contains(day), false, false, false, false);
}
