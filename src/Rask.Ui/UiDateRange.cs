using System.Runtime.InteropServices;

namespace Rask;

/// <summary>
/// A stretch of days, first to last, both included — what a range calendar and a range date picker bind.
/// Flux UI's <c>DateRange</c>.
/// </summary>
/// <remarks>
/// <para>
/// Always whole. The reader's first click is held by the control and drawn as the start, and the model only
/// changes once the second click gives the range an end — so a bound model never holds half a range.
/// </para>
/// <para>
/// "Nothing chosen" is <c>default(UiDateRange)</c>, as it is <c>default(DateOnly)</c> for a single day — bind a
/// <c>UiDateRange?</c> where unset and a real range must differ.
/// </para>
/// </remarks>
/// <param name="Start">The first day in the range.</param>
/// <param name="End">The last day in the range. The same as <paramref name="Start" /> for a range of one day.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct UiDateRange(DateOnly Start, DateOnly End)
{
    /// <summary>The preset this range was made from, when a date picker's preset list made it.</summary>
    public Ui.DateRangePreset? Preset { get; init; }

    /// <summary>How many days the range holds, its ends included; none for an unset range.</summary>
    public int Count => this == default ? 0 : End.DayNumber - Start.DayNumber + 1;

    /// <summary>Whether <paramref name="date" /> falls inside the range, its ends included.</summary>
    public bool Contains(DateOnly date) => this != default && date >= Start && date <= End;

    /// <summary>The range between two days, in whichever order they were picked.</summary>
    public static UiDateRange Between(DateOnly a, DateOnly b) => a <= b ? new(a, b) : new(b, a);

    /// <summary>The range a preset names, counted from <paramref name="today" />.</summary>
    /// <param name="preset">Which of Flux's presets.</param>
    /// <param name="today">The day the preset is counted from.</param>
    /// <param name="startDay">The first day of the week, for the week presets.</param>
    /// <param name="min">Where <see cref="Ui.DateRangePreset.AllTime" /> starts.</param>
    public static UiDateRange Of(Ui.DateRangePreset preset, DateOnly today, DayOfWeek startDay = DayOfWeek.Sunday, DateOnly? min = null) =>
        UiDateRangePresets.Resolve(preset, today, startDay, min) with { Preset = preset };
}
