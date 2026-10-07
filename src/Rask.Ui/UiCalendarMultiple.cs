using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// A month, with several days to pick — Flux UI's <c>flux:calendar multiple</c>.
/// </summary>
/// <remarks>
/// Opened by Flux's mode step on the calendar's entry: <c>Ui.Calendar.Multiple.Bind(() =&gt; model.DaysOff)</c>
/// over any collection of <c>DateOnly</c>, or <c>Ui.Calendar.Multiple.Values([...])</c> with <c>OnChange</c>. A click
/// adds a day, a second click takes it out again, and the collection is kept in date order.
/// </remarks>
public sealed partial class UiCalendarMultiple : UiCalendarControl<ICollection<DateOnly>>
{
    private protected override Ui.CalendarMode Bound => Ui.CalendarMode.Multiple;

    private protected override UiCalendarPicks Picks(ICollection<DateOnly>? current, Func<ICollection<DateOnly>, Task> commit) =>
        new UiCalendarMultiplePick(current ?? [], picked => commit([.. picked]));

    private protected override Task CommitAsync(
        ExpressionAccessor.Accessor? accessor, EditContext? context, ICollection<DateOnly> value) =>
        UiFormCommit.CommitSelectionAsync(this, accessor, context, [.. value]);
}
