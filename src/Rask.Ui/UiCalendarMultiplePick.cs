namespace Rask;

/// <summary>Several days chosen; a click adds a day or takes it out again.</summary>
internal sealed class UiCalendarMultiplePick(ICollection<DateOnly> chosen, Func<IReadOnlyList<DateOnly>, Task> commit) : UiCalendarPicks
{
    internal override DateOnly? First => chosen.Count > 0 ? chosen.Min() : null;

    internal override UiCalendarMarks Marks(DateOnly day) => new(chosen.Contains(day), false, false, false, false);

    internal override async Task<bool> PickAsync(DateOnly day)
    {
        var next = chosen.Where(d => d != day).ToList();
        if (next.Count == chosen.Count)
        {
            next.Add(day);
        }

        next.Sort();
        await commit(next).ConfigureAwait(false);
        return false;
    }

    internal override bool Finishes(DateOnly day) => false;
}
