namespace Rask.Background;

/// <summary>
///     Which work a bell shows, in which order — the board's read and the kit's merge of it with what is heard live alike:
///     what runs, newest first, then the <see cref="EndedShown" /> latest ended pieces of work, newest first. A batch
///     (<see cref="WorkReported.Batch" />) is one piece, kept whole; one still running keeps its ended jobs beside its running ones.
/// </summary>
internal static class WorkShown
{
    /// <summary>How many ended pieces of work are shown at most: a batch counts once.</summary>
    internal const int EndedShown = 10;

    /// <summary>The work to show of <paramref name="work" />, in order.</summary>
    internal static IReadOnlyList<WorkReported> Of(IEnumerable<WorkReported> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var all = work as IReadOnlyCollection<WorkReported> ?? [.. work];
        var running = all.Where(w => !w.HasEnded).OrderByDescending(w => w.Id).ToList();
        var open = running.Where(w => w.Batch is not null).Select(w => w.Batch!.Value).ToHashSet();
        var ended = all.Where(w => w.HasEnded).OrderByDescending(w => w.At).ToList();
        var kept = ended
            .Where(w => w.Batch is not { } batch || !open.Contains(batch))
            .GroupBy(w => w.Batch is { } batch ? (object)batch : w.Id)
            .Take(EndedShown)
            .SelectMany(group => group);
        return [.. running, .. ended.Where(w => w.Batch is { } batch && open.Contains(batch)), .. kept];
    }
}
