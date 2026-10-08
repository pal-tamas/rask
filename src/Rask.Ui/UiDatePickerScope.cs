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

    internal bool Open { get; init; }

    internal bool Disabled { get; init; }

    internal bool Invalid { get; init; }

    internal IReadOnlyDictionary<string, string?> Aria { get; init; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>The chosen day, or a range's start and end: one typed field each.</summary>
    internal required IReadOnlyList<DateOnly?> Dates { get; init; }

    /// <summary>Writes a typed date into its slot.</summary>
    internal required Func<int, DateOnly, Task> Typed { get; init; }

    /// <summary>Opens the calendar from a key on the trigger, where the browser has no gesture of its own for it.</summary>
    internal required Action Show { get; init; }

    /// <summary>The next typed field's slot: the first is the day or a range's start, the second its end.</summary>
    internal int Claim() => Math.Min(_claimed++, Dates.Count - 1);
}
