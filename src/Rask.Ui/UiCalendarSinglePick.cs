namespace Rask;

/// <summary>One day chosen.</summary>
internal sealed class UiCalendarSinglePick(DateOnly chosen) : UiCalendarPicks
{
    internal override DateOnly? First => chosen == default ? null : chosen;

    internal override string Mode => "single";

    internal override UiCalendarMarks Marks(DateOnly day) => new(chosen != default && day == chosen, false, false, false, false);
}
