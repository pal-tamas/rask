namespace Rask;

/// <summary>What "chosen" means — one day, several, or a range — and what a click on a day does.</summary>
internal abstract class UiCalendarPicks
{
    /// <summary>The day a calendar opens on and Tab lands on: the first one chosen.</summary>
    internal abstract DateOnly? First { get; }

    /// <summary>A range marks days with no <c>aria-selected</c> at all until they are in it.</summary>
    internal virtual bool Range => false;

    internal abstract UiCalendarMarks Marks(DateOnly day);

    /// <summary>Picks <paramref name="day" />; answers whether that finished the choice.</summary>
    internal abstract Task<bool> PickAsync(DateOnly day);

    /// <summary>Whether a pick of <paramref name="day" /> would finish the choice, known before the click.</summary>
    internal abstract bool Finishes(DateOnly day);

    /// <summary>A day the selection itself rules out: too near or too far from a waiting range's start.</summary>
    internal virtual bool Blocks(DateOnly day) => false;
}
