using System.Runtime.InteropServices;

namespace Rask;

/// <summary>
/// A stretch of the day, first minute to last, both included — what Flux writes as <c>05:30-07:29</c> in a time
/// picker's <c>unavailable</c>.
/// </summary>
/// <remarks>
/// A single time converts to the stretch that is only that minute, so a list reads as Flux's does:
/// <c>[new TimeOnly(3, 0), new UiTimeRange(new(5, 30), new(7, 29))]</c>.
/// </remarks>
/// <param name="Start">The first minute in the stretch.</param>
/// <param name="End">The last minute in the stretch.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct UiTimeRange(TimeOnly Start, TimeOnly End)
{
    /// <summary>Whether <paramref name="time" /> falls inside the stretch, its ends included.</summary>
    public bool Contains(TimeOnly time) => time >= Start && time <= End;

    /// <summary>One minute as a stretch.</summary>
    public static UiTimeRange FromTimeOnly(TimeOnly time) => new(time, time);

    /// <summary>One minute as a stretch.</summary>
    public static implicit operator UiTimeRange(TimeOnly time) => FromTimeOnly(time);
}
