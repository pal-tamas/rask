namespace Rask;

/// <summary>
/// Two months, with a range of days to pick — Flux UI's <c>flux:calendar mode="range"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Opened by Flux's mode step on the calendar's entry: <c>Ui.Calendar.Range.Bind(() =&gt; model.Stay)</c> over
/// a <see cref="UiDateRange" /> (or a <c>UiDateRange?</c>), or <c>Ui.Calendar.Range.Value(range).OnChange(…)</c>.
/// </para>
/// <para>
/// The first click is the start and is held by the calendar; the stretch to the day under the pointer or the
/// keyboard is drawn as it would be, and the second click — on that day or a later one — writes the whole
/// range. A click before the start begins again from there. <c>MinRange</c> and <c>MaxRange</c> rule out the
/// days that would make the range too short or too long.
/// </para>
/// </remarks>
public sealed partial class UiCalendarRange : UiCalendarControl<UiDateRange>
{
    private protected override Ui.CalendarMode Bound => Ui.CalendarMode.Range;

    private protected override int DefaultMonths => 2;

    private protected override UiCalendarPicks Picks(UiDateRange current, Func<UiDateRange, Task> commit) =>
        new UiCalendarRangePick(current, State, MinRange, MaxRange, commit);
}
