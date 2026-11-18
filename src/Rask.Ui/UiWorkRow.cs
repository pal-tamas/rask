using System.Globalization;
using Rask.Background;
using Rask.Core.Globalization;

namespace Rask;

/// <summary>
///     One job as a <see cref="UiWorkNotices" /> flyout and a <see cref="UiWorkList" /> draw it: its title, then — while it runs —
///     Flux's progress bar filled to its count (one that moves without an end when it gave none) over what it last said; once it
///     has ended, a badge that says how, and what its handler said of it (<see cref="WorkReported.Detail" />) coloured by how.
///     The jobs of one batch (<see cref="WorkReported.Batch" />) are drawn as one group: when it was started and how many it
///     holds, the worst way one of them ended, one bar for them all, then each job.
/// </summary>
[Rask.Core.RaskMarkup]
internal static partial class UiWorkRow
{
    /// <summary>The rows of <paramref name="work" />, or the line that says there is none.</summary>
    internal static Component Rows(IReadOnlyList<WorkReported> work, string? @class = null) =>
        work.Count == 0
            ? Ui.Text.Class("py-3", @class).Attributes(("data-ui-work-none", ""))[RaskStrings.Get(RaskString.WorkNone, "No background work.")]
            : Ul.Class("divide-y divide-zinc-800/10 dark:divide-white/20", @class).Attributes(("data-ui-work-list", ""))[
                Pieces(work).Select(piece => piece.Count == 1 ? Row(piece[0]) : Group(piece))];

    /// <summary>
    ///     <paramref name="work" /> in its order, the jobs of a batch gathered where its first one stands: a batch of one is a
    ///     job like any other.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<WorkReported>> Pieces(IReadOnlyList<WorkReported> work)
    {
        var batches = work.Where(w => w.Batch is not null).ToLookup(w => w.Batch!.Value);
        var drawn = new HashSet<Guid>();
        var pieces = new List<IReadOnlyList<WorkReported>>();
        foreach (var w in work)
        {
            if (w.Batch is not { } batch)
            {
                pieces.Add([w]);
            }
            else if (drawn.Add(batch))
            {
                pieces.Add([.. batches[batch]]);
            }
        }

        return pieces;
    }

    /// <summary>How a batch stands: running while one of it runs, else the worst way one of it ended.</summary>
    internal static WorkState Worst(IEnumerable<WorkReported> batch) =>
        batch.Select(w => w.State).MaxBy(Severity);

    /// <summary>How bad an end is, worst highest: failed, warned, stopped, done — and running above them all, as not over yet.</summary>
    internal static int Severity(WorkState state) => state switch
    {
        WorkState.Running => 4,
        WorkState.Failed => 3,
        WorkState.Warned => 2,
        WorkState.Stopped => 1,
        _ => 0,
    };

    /// <summary>One job.</summary>
    internal static Component Row(WorkReported work) =>
        Li.Key(work.Id).Class("flex flex-col gap-2 py-3").Attributes(("data-ui-work", State(work.State)))[
            Div.Class("flex items-center justify-between gap-3")[
                Span.Class("min-w-0 truncate text-sm font-medium text-zinc-800 dark:text-white")[work.Title],
                work.HasEnded ? Ended(work.State) : Count(work.Done, work.Of)
            ],
            work.HasEnded ? null : Bar(work.Done, work.Of, work.Title),
            Words(work),
            Detail(work)
        ];

    // A batch: when it was started and how many it holds, how it stands, one bar for all of it, then its jobs.
    private static Component Group(IReadOnlyList<WorkReported> batch)
    {
        var state = Worst(batch);
        var heading = RaskStrings.Get(
            RaskString.WorkBatch, "{0} · {1} items", Started(batch), batch.Count.ToString(CultureInfo.CurrentCulture));
        var (done, of) = Together(batch);
        return Li.Key($"batch-{batch[0].Batch}").Class("flex flex-col gap-2 py-3")
            .Attributes(("data-ui-work-batch", State(state)), ("data-ui-work", State(state)))[
                Div.Class("flex items-center justify-between gap-3")[
                    Span.Class("min-w-0 truncate text-sm font-medium text-zinc-800 dark:text-white")[heading],
                    state == WorkState.Running ? Count(done, of) : Ended(state)
                ],
                state == WorkState.Running ? Bar(done, of, heading) : null,
                Ul.Class("flex flex-col border-s border-zinc-800/10 ps-3 dark:border-white/20")[
                    batch.Select(Member)]
            ];
    }

    // One job of a batch: its title and how it ended, under the batch's own bar.
    private static Component Member(WorkReported work) =>
        Li.Key(work.Id).Class("flex flex-col gap-1 py-1").Attributes(("data-ui-work-item", State(work.State)))[
            Div.Class("flex items-center justify-between gap-3")[
                Span.Class("min-w-0 truncate text-sm text-zinc-800 dark:text-white")[work.Title],
                work.HasEnded ? Ended(work.State) : Count(work.Done, work.Of)
            ],
            Detail(work)
        ];

    // The batch as one count: every job's own when each gave one — an ended job counts whole — else how many have ended.
    private static (int Done, int Of) Together(IReadOnlyList<WorkReported> batch) =>
        batch.All(w => w.Of is > 0)
            ? (batch.Sum(w => w.HasEnded ? w.Of!.Value : Math.Min(w.Done ?? 0, w.Of!.Value)), batch.Sum(w => w.Of!.Value))
            : (batch.Count(w => w.HasEnded), batch.Count);

    // When its first job was started, in the reader's own words for a date and time.
    private static string Started(IReadOnlyList<WorkReported> batch) =>
        batch.Min(w => w.At).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static Component? Count(int? done, int? of) =>
        done is { } d && of is > 0 and int total
            ? Ui.Text.Size(Ui.TextSize.Sm).Class("shrink-0 tabular-nums")[
                RaskStrings.Get(RaskString.WorkProgress, "{0} of {1}", Number(d), Number(total))]
            : null;

    // Flux's bar to its count; without one, a third of the track that pulses, and no value a reader would take for a share.
    private static Component Bar(int? done, int? of, string label)
    {
        Component bar = done is { } d && of is > 0 and int total
            ? Ui.Progress.Value(Math.Min(d, total)).Max(total).Aria("label", label)
            : Div.Role("progressbar").Aria("label", label)
                .Class("relative block h-1.5 w-full overflow-hidden rounded-full bg-zinc-200 dark:bg-white/10")
                .Attributes(("data-ui-work-unknown", ""))[
                    Div.Class("absolute inset-y-0 start-0 w-1/3 rounded-full bg-accent motion-safe:animate-pulse")
                ];
        return bar;
    }

    private static Component? Words(WorkReported work)
    {
        var words = work.Progress ?? (work.HasEnded || work.Of is > 0 ? null : RaskStrings.Get(RaskString.WorkRunning, "Running…"));
        return words is null ? null : Ui.Text.Size(Ui.TextSize.Sm).Class("truncate")[words];
    }

    // What its handler said of how it ended, in the colour of how: never the error a failure threw — that is not in it.
    private static Component? Detail(WorkReported work) =>
        work is { HasEnded: true, Detail: { Length: > 0 } detail }
            ? Ui.Text.Size(Ui.TextSize.Sm).Class(DetailColour(work.State)).Attributes(("data-ui-work-detail", ""))[detail]
            : null;

    private static string DetailColour(WorkState state) => state switch
    {
        WorkState.Warned => "text-amber-700 dark:text-amber-400",
        WorkState.Failed => "text-red-700 dark:text-red-400",
        _ => "text-zinc-500 dark:text-white/70",
    };

    // Never why it failed: that stays on the operator's board.
    private static Component Ended(WorkState state) => state switch
    {
        WorkState.Done => Ui.Badge.Size(Ui.BadgeSize.Sm).Color(Ui.Color.Green).Icon(Ui.IconName.CheckCircle)[
            RaskStrings.Get(RaskString.WorkDone, "Done")],
        WorkState.Warned => Ui.Badge.Size(Ui.BadgeSize.Sm).Color(Ui.Color.Amber).Icon(Ui.IconName.ExclamationTriangle)[
            RaskStrings.Get(RaskString.WorkWarned, "Warning")],
        WorkState.Failed => Ui.Badge.Size(Ui.BadgeSize.Sm).Color(Ui.Color.Red).Icon(Ui.IconName.ExclamationCircle)[
            RaskStrings.Get(RaskString.WorkFailed, "Failed")],
        _ => Ui.Badge.Size(Ui.BadgeSize.Sm).Icon(Ui.IconName.StopCircle)[RaskStrings.Get(RaskString.WorkStopped, "Stopped")],
    };

    private static string State(WorkState state) => state switch
    {
        WorkState.Done => "done",
        WorkState.Warned => "warned",
        WorkState.Failed => "failed",
        WorkState.Stopped => "stopped",
        _ => "running",
    };

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
