namespace Rask;

/// <summary>A range chosen: the first click is held as its start, the second writes it.</summary>
internal sealed class UiCalendarRangePick(
    UiDateRange chosen, UiCalendarState state, int? minRange, int? maxRange, Func<UiDateRange, Task> commit) : UiCalendarPicks
{
    internal override DateOnly? First => state.Anchor ?? (chosen == default ? null : chosen.Start);

    internal override bool Range => true;

    internal override UiCalendarMarks Marks(DateOnly day)
    {
        if (state.Anchor is not { } anchor)
        {
            return chosen == default
                ? default
                : new(day == chosen.Start || day == chosen.End, chosen.Contains(day), day == chosen.Start, day == chosen.End, false);
        }

        // Waiting for its end: the stretch to the day under the pointer or the keyboard is drawn as it would be.
        var hover = state.Hover is { } h && h != anchor ? h : (DateOnly?)null;
        var ahead = hover is { } to && to > anchor;
        return new(day == anchor, ahead && day >= anchor && day <= hover, day == anchor, false, day == hover);
    }

    internal override async Task<bool> PickAsync(DateOnly day)
    {
        if (state.Anchor is not { } anchor || day < anchor)
        {
            (state.Anchor, state.Hover) = (day, null);
            return false;
        }

        (state.Anchor, state.Hover) = (null, null);
        await commit(new UiDateRange(anchor, day)).ConfigureAwait(false);
        return true;
    }

    internal override bool Finishes(DateOnly day) => state.Anchor is { } anchor && day >= anchor;

    // With a limit set, Flux rules out every day before a waiting start and every day that would make the
    // range shorter or longer than allowed.
    internal override bool Blocks(DateOnly day)
    {
        if (state.Anchor is not { } anchor || (minRange is null && maxRange is null) || day == anchor)
        {
            return false;
        }

        var count = day.DayNumber - anchor.DayNumber + 1;
        return day < anchor || count < (minRange ?? 1) || count > (maxRange ?? int.MaxValue);
    }
}
