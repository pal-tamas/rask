using System.Globalization;

namespace Rask;

/// <summary>
/// What a date picker tells its trigger — <see cref="UiDatePickerButton" /> or <see cref="UiDatePickerInput" />,
/// drawn by the picker or written into its <c>Trigger</c> slot.
/// </summary>
internal sealed class UiDatePickerScope
{
    private int _claimed;

    internal required string PopoverId { get; init; }

    internal required string CalendarId { get; init; }

    internal required string ControlId { get; init; }

    internal required string Placeholder { get; init; }

    internal required CultureInfo Culture { get; init; }

    /// <summary>What the button shows: the chosen day, range or preset; nothing while unset.</summary>
    internal string? Text { get; init; }


    internal bool Disabled { get; init; }

    internal bool Invalid { get; init; }

    internal IReadOnlyDictionary<string, string?> Aria { get; init; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Whether the picker asked for the button that empties it, on whichever trigger it has.</summary>
    internal bool Clearable { get; init; }

    /// <summary>Empties the picker: nothing chosen.</summary>
    internal required Func<Task> Clear { get; init; }

    /// <summary>The chosen day, or a range's start and end: one typed field each.</summary>
    internal required IReadOnlyList<DateOnly?> Dates { get; init; }

    /// <summary>Writes a typed date into its slot.</summary>
    internal required Func<int, DateOnly, Task> Typed { get; init; }

    /// <summary>
    ///     Hears that a typed field was left holding no date — its slot, whether it is half filled rather than
    ///     emptied, and how many typed fields the trigger has.
    /// </summary>
    internal required Func<int, bool, int, Task> Untyped { get; init; }

    /// <summary>
    ///     What a typed field announced, heard by the picker against the fields as it last drew them: the one
    ///     handler a typed field registers, the same in every render (the scope itself is new in each).
    /// </summary>
    internal required Func<int, string, Task> Heard { get; init; }

    /// <summary>What a typed field's parts carry while a message shows: the mark that hears the first key of a correction.</summary>
    internal (string Name, string? Value)[] FirstEdit { get; init; } = [];

    /// <summary>A typed field was left with no date in it: half filled, or with every part emptied.</summary>
    internal Task Left(int slot, bool half) => Untyped(slot, half, Math.Max(1, _claimed));

    /// <summary>Whether a typed whole date waits for the reader's next action: the picker is bound and not live.</summary>
    internal bool Waits { get; init; }

    /// <summary>The next typed field's slot: the first is the day or a range's start, the second its end.</summary>
    internal int Claim() => Math.Min(_claimed++, Dates.Count - 1);

    /// <summary>
    ///     The × a trigger writes before its chevron, where the trigger or the picker asked for one: shown while a
    ///     date is chosen and the trigger is not <paramref name="locked" />, and by the runtime as one is picked.
    /// </summary>
    internal Rask.Core.Component? ClearInside(bool? asked, bool locked) =>
        asked ?? Clearable ? UiPickerClear.Inside(Clear, Dates[0] is not null && !locked, marks: UiCalendarHook.Named("clear")) : null;
}
