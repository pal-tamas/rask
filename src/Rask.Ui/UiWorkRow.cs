using System.Globalization;
using Rask.Background;
using Rask.Core.Globalization;

namespace Rask;

/// <summary>
///     One job as a <see cref="UiWorkNotices" /> flyout and a <see cref="UiWorkList" /> draw it: its title, then — while it runs —
///     Flux's progress bar filled to its count (one that moves without an end when it gave none) over what it last said; once it
///     has ended, a badge that says how.
/// </summary>
[Rask.Core.RaskMarkup]
internal static partial class UiWorkRow
{
    /// <summary>The rows of <paramref name="work" />, or the line that says there is none.</summary>
    internal static Component Rows(IReadOnlyList<WorkReported> work, string? @class = null) =>
        work.Count == 0
            ? Ui.Text.Class("py-3", @class).Attributes(("data-ui-work-none", ""))[RaskStrings.Get(RaskString.WorkNone, "No background work.")]
            : Ul.Class("divide-y divide-zinc-800/10 dark:divide-white/20", @class).Attributes(("data-ui-work-list", ""))[
                work.Select(Row)];

    /// <summary>One job.</summary>
    internal static Component Row(WorkReported work) =>
        Li.Key(work.Id).Class("flex flex-col gap-2 py-3").Attributes(("data-ui-work", State(work.State)))[
            Div.Class("flex items-center justify-between gap-3")[
                Span.Class("min-w-0 truncate text-sm font-medium text-zinc-800 dark:text-white")[work.Title],
                work.HasEnded ? Ended(work.State) : Count(work)
            ],
            work.HasEnded ? null : Bar(work),
            Words(work)
        ];

    private static Component? Count(WorkReported work) =>
        work is { Done: { } done, Of: > 0 and var of }
            ? Ui.Text.Size(Ui.TextSize.Sm).Class("shrink-0 tabular-nums")[
                RaskStrings.Get(RaskString.WorkProgress, "{0} of {1}", Number(done), Number(of))]
            : null;

    // Flux's bar to its count; without one, a third of the track that pulses, and no value a reader would take for a share.
    private static Component Bar(WorkReported work)
    {
        Component bar = work is { Done: { } done, Of: > 0 and var of }
            ? Ui.Progress.Value(Math.Min(done, of)).Max(of).Aria("label", work.Title)
            : Div.Role("progressbar").Aria("label", work.Title)
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

    // Never why it failed: that stays on the operator's board.
    private static Component Ended(WorkState state) => state switch
    {
        WorkState.Done => Ui.Badge.Size(Ui.BadgeSize.Sm).Color(Ui.Color.Green).Icon(Ui.IconName.CheckCircle)[
            RaskStrings.Get(RaskString.WorkDone, "Done")],
        WorkState.Failed => Ui.Badge.Size(Ui.BadgeSize.Sm).Color(Ui.Color.Red).Icon(Ui.IconName.ExclamationCircle)[
            RaskStrings.Get(RaskString.WorkFailed, "Failed")],
        _ => Ui.Badge.Size(Ui.BadgeSize.Sm).Icon(Ui.IconName.StopCircle)[RaskStrings.Get(RaskString.WorkStopped, "Stopped")],
    };

    private static string State(WorkState state) => state switch
    {
        WorkState.Done => "done",
        WorkState.Failed => "failed",
        WorkState.Stopped => "stopped",
        _ => "running",
    };

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
