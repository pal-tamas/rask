using Rask.Background;
using Rask.Core.Authentication;
using Rask.Core.Live;
using Rask.Cqrs;
using Rask.Querying;

namespace Rask;

/// <summary>
///     The work a <see cref="UiWorkNotices" /> and a <see cref="UiWorkList" /> show: the rows the signed-in user may see — their
///     own, and their tenant's shared work — read again on every poll of the job processor, with what their jobs publish
///     (<see cref="WorkReported" />) moving them in between.
/// </summary>
/// <remarks>
///     A subscription is one process's, so the rows are the truth — work on another host, or that ended while nobody watched,
///     is on the screen within one read — and the events only make the bar move between reads. With no rows to read (no
///     Rask.Jobs in this app) and no events admitted, it is nothing: the bell stands quiet rather than failing the page.
/// </remarks>
internal static class UiWorkFeed
{
    /// <summary>How many ended pieces of work are shown at most, newest first: a batch counts once.</summary>
    internal const int EndedShown = WorkShown.EndedShown;

    // Enough for the latest of every job a user watches at once; older values of one job are superseded anyway.
    private const int Kept = 64;

    /// <summary>The clock the bell counts news by. Moved in tests.</summary>
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>
    ///     What runs or waits — newest first — then what ended lately, newest first. Called in <c>Render</c>: the query and
    ///     the subscription are the component's own, and each value that lands draws it again.
    /// </summary>
    /// <param name="lane">One lane's work, or null for all of it.</param>
    internal static IReadOnlyList<WorkReported> Read(string? lane)
    {
        var services = LiveRenderContext.Current?.Services;
        if (services?.GetService(typeof(IQueryClient)) is null)
        {
            return [];
        }

        var rows = services.GetService(typeof(IWorkBoard)) is IWorkBoard board
            ? QueryClient.Query(
                QueryKey.Of("rask", "work", lane ?? ""),
                ct => board.Mine(lane, ct),
                new QueryOptions { RefetchInterval = board.Every }).Data
            : null;
        var live = QueryClient.Subscribe(new WatchOwnWork(lane)).Keep(Kept).Items;
        var viewer = (services.GetService(typeof(IUserProvider)) as IUserProvider)?.Current.UserId();
        return Merge(rows ?? [], live, viewer);
    }

    /// <summary>
    ///     The latest word on each job — an end beats a run, and a later word beats an earlier one — each saying whether
    ///     <paramref name="viewer" /> started it, in the order <see cref="WorkShown" /> shows.
    /// </summary>
    internal static IReadOnlyList<WorkReported> Merge(IEnumerable<WorkReported> rows, IEnumerable<WorkReported> live, Guid? viewer = null)
    {
        var latest = new Dictionary<long, WorkReported>();
        foreach (var work in rows.Concat(live))
        {
            latest[work.Id] = latest.TryGetValue(work.Id, out var had) ? Later(had, work) : work;
        }

        return WorkShown.Of(latest.Values.Select(w => w.Mine is null ? w with { Mine = w.IsMineTo(viewer) } : w));
    }

    // Whose it is, once known, is kept: a shared job heard live says nothing of it, and the board's read did.
    private static WorkReported Later(WorkReported had, WorkReported heard)
    {
        var later = Newer(had, heard);
        var other = ReferenceEquals(later, heard) ? had : heard;
        return later.Mine is null && other.Mine is { } mine ? later with { Mine = mine } : later;
    }

    private static WorkReported Newer(WorkReported had, WorkReported heard)
    {
        if (had.HasEnded != heard.HasEnded)
        {
            return had.HasEnded ? had : heard;
        }

        return heard.At >= had.At ? heard : had;
    }
}
