namespace Rask;

/// <summary>One day chosen; a second click on it clears it.</summary>
internal sealed class UiCalendarSinglePick(DateOnly chosen, Func<DateOnly, Task> commit) : UiCalendarPicks
{
    internal override DateOnly? First => chosen == default ? null : chosen;

    internal override UiCalendarMarks Marks(DateOnly day) => new(chosen != default && day == chosen, false, false, false, false);

    // A second click on the chosen day clears it, as Flux does.
    internal override async Task<bool> PickAsync(DateOnly day)
    {
        await commit(day == chosen ? default : day).ConfigureAwait(false);
        return day != chosen;
    }

    internal override bool Finishes(DateOnly day) => day != chosen;
}
