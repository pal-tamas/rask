using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// A month, with several days to pick — Flux UI's <c>flux:calendar multiple</c>.
/// </summary>
/// <remarks>
/// Reached through the same entry as <see cref="UiCalendar" />: <c>Ui.Calendar.Bind(() =&gt; model.DaysOff)</c>
/// over any collection of <c>DateOnly</c>, or <c>Ui.Calendar.Values([...])</c> with <c>OnChange</c>. A click
/// adds a day, a second click takes it out again, and the collection is kept in date order.
/// </remarks>
[RaskChainEntry("UiCalendar")]
public sealed partial class UiCalendarMultiple : UiCalendarControl<ICollection<DateOnly>>
{
    private protected override UiCalendarPicks Picks(ICollection<DateOnly>? current, Func<ICollection<DateOnly>, Task> commit) =>
        new UiCalendarMultiplePick(current ?? [], picked => commit([.. picked]));

    private protected override Task CommitAsync(
        ExpressionAccessor.Accessor? accessor, EditContext? context, ICollection<DateOnly> value) =>
        UiFormCommit.CommitSelectionAsync(this, accessor, context, [.. value]);
}
