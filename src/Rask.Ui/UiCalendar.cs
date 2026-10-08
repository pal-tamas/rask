namespace Rask;

/// <summary>
/// A month, with a day to pick — Flux UI's <c>flux:calendar</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Ui.Calendar.Bind(() =&gt; model.Delivery)</c> two-way binds a <c>DateOnly</c> (or a <c>DateOnly?</c>) and
/// drives the surrounding <c>Form</c>'s validation; <c>.Value(day).OnChange(…)</c> leaves it with the parent.
/// "Nothing chosen" is <c>default(DateOnly)</c> — <c>null</c> through a nullable binding — and a second click
/// on the chosen day goes back to it.
/// </para>
/// <para>
/// <b>Flux's mode is the step before it</b>: <c>Ui.Calendar.Multiple</c> opens <see cref="UiCalendarMultiple" />
/// over a collection of days, <c>Ui.Calendar.Range</c> opens <see cref="UiCalendarRange" /> over a <see cref="UiDateRange" />.
/// Every prop is on <see cref="UiCalendarControl{T}" />.
/// </para>
/// <para>
/// The days are buttons in a grid: one of them is in the Tab order, the arrows walk days and weeks into the
/// neighbouring months, PageUp/PageDown and Home/End page a month, and Enter or Space picks.
/// </para>
/// </remarks>
public sealed partial class UiCalendar : UiCalendarControl<DateOnly>
{
    private protected override Ui.CalendarMode Bound => Ui.CalendarMode.Single;

    private protected override UiCalendarPicks Picks(DateOnly current, Func<DateOnly, Task> commit) =>
        new UiCalendarSinglePick(current, commit);
}
