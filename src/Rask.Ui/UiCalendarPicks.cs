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

    /// <summary>Every day chosen: none, one, several, or a range's start and end.</summary>
    private protected abstract IEnumerable<DateOnly> Days { get; }

    /// <summary>
    ///     Whether the calendar could have drawn this choice (#1272): every day of it one the reader can press, within
    ///     <c>Min</c> and <c>Max</c> and not <c>Unavailable</c>, as <c>rask-calendar.ts</c> rules them. A choice of
    ///     nothing is a clear, which every calendar offers.
    /// </summary>
    internal virtual bool DrawnBy(UiCalendarOptions options) => Days.All(options.Offers);

    /// <summary>
    ///     What the field says of this choice when the reader typed it outside the limits (#1273) — a day before
    ///     <c>Min</c>, after <c>Max</c> or <c>Unavailable</c> — or null when every day of it is one the calendar offers.
    /// </summary>
    internal virtual string? Refusal(UiCalendarOptions options) =>
        Days.Select(day => UiPickerLimits.Day(day, options)).FirstOrDefault(said => said is not null);

    /// <summary>
    ///     What the field says of a choice the calendar drew whose length it does not hold to <c>MinRange</c> and
    ///     <c>MaxRange</c> — a range typed into its own inputs — or null: only a range has a length.
    /// </summary>
    internal virtual string? LengthRefusal(UiCalendarOptions options) => null;
}
