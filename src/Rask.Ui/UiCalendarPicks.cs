namespace Rask;

/// <summary>What "chosen" means — one day, several, or a range — as the page's first drawing of a calendar shows it.</summary>
/// <remarks>Everything a reader does to it after that is the browser's (<c>rask-calendar.ts</c>).</remarks>
internal abstract class UiCalendarPicks
{
    /// <summary>The day a calendar opens on and Tab lands on: the first one chosen.</summary>
    internal abstract DateOnly? First { get; }

    /// <summary>A range marks days with no <c>aria-selected</c> at all until they are in it.</summary>
    internal virtual bool Range => false;

    /// <summary>The mode as the browser reads it: <c>single</c>, <c>multiple</c> or <c>range</c>.</summary>
    internal abstract string Mode { get; }

    internal abstract UiCalendarMarks Marks(DateOnly day);

    /// <summary>What the calendar's own inputs show (<c>with-inputs</c>): the day, or a range's start and end.</summary>
    internal virtual IReadOnlyList<DateOnly?> Typed => [First];
}
