namespace Rask.Ui;

/// <summary>
/// A stretch of days, first to last, both included — what <see cref="UiCalendarRange" /> and
/// <see cref="UiDatePickerRange" /> bind.
/// </summary>
/// <remarks>
/// <para>
/// Always whole. The reader's first click is held by the control and drawn as the start, and the model only
/// changes once the second click gives the range an end — so a bound model never holds half a range, and nothing
/// reading it has to ask whether the end is there yet.
/// </para>
/// <para>
/// <see cref="Start" /> is never after <see cref="End" />: a reader who clicks the later day first gets the same
/// range as one who clicks the earlier day first. "Nothing chosen" is <c>default(UiDateRange)</c>, as it is
/// <c>default(DateOnly)</c> for a single day — bind a <c>UiDateRange?</c> where unset and a real range must differ.
/// </para>
/// </remarks>
/// <param name="Start">The first day in the range.</param>
/// <param name="End">The last day in the range. The same as <paramref name="Start" /> for a range of one day.</param>
public readonly record struct UiDateRange(DateOnly Start, DateOnly End)
{
    /// <summary>Whether <paramref name="date" /> falls inside the range, its ends included.</summary>
    public bool Contains(DateOnly date) => this != default && date >= Start && date <= End;

    /// <summary>The range between two days, in whichever order they were picked.</summary>
    public static UiDateRange Between(DateOnly a, DateOnly b) => a <= b ? new(a, b) : new(b, a);
}
