using System.Globalization;
using Rask.Background;
using Rask.Core;
using Rask.Core.Globalization;
using Rask.Core.Messaging;

namespace Rask;

/// <summary>
///     What a <see cref="UiWorkNotices" /> or a <see cref="UiWorkList" /> says to a screen reader when a job it has seen running
///     ends — "Import of prices.csv is done." — in a polite live region, so it waits for the reader to finish what it reads; and,
///     for the bell, the one toast a batch its reader started raises when its last job ends (#1368).
/// </summary>
/// <remarks>
///     Only an end it saw coming is said, and toasted: a page opened after a job ended shows it ended, and says nothing. A
///     colleague's shared batch is shown, never toasted.
/// </remarks>
[Rask.Core.RaskMarkup]
internal sealed partial class UiWorkAnnouncer
{
    private readonly HashSet<long> _running = [];
    private readonly HashSet<Guid> _openBatches = [];

    /// <summary>Whether a batch's end raises a toast: the bell's does; a list in the page, beside a bell, says it in words only.</summary>
    internal bool Toasts { get; init; }

    /// <summary>What it says now, or null.</summary>
    internal string? Said { get; private set; }

    /// <summary>Hears the work as it is shown now: an end of a job last seen running is what it says next.</summary>
    internal void Hear(IReadOnlyList<WorkReported> work)
    {
        foreach (var w in work)
        {
            if (!w.HasEnded)
            {
                _running.Add(w.Id);
            }
            else if (_running.Remove(w.Id))
            {
                Said = Ended(w);
            }
        }

        if (Toasts)
        {
            ToastEndedBatches(work);
        }
    }

    /// <summary>The live region: always on the page, so a reader hears the words it takes.</summary>
    internal Component Region() =>
        Div.Class("sr-only").Role("status").Aria("live", "polite").Attributes(("data-ui-work-said", ""))[Said];

    // A batch of the reader's own seen running, now with nothing left to run: one toast, in the tone of its worst end.
    private void ToastEndedBatches(IReadOnlyList<WorkReported> work)
    {
        foreach (var batch in work.Where(w => w.Batch is not null).GroupBy(w => w.Batch!.Value))
        {
            if (batch.Any(w => !w.HasEnded))
            {
                if (batch.Any(w => w.Mine == true))
                {
                    _openBatches.Add(batch.Key);
                }
            }
            else if (_openBatches.Remove(batch.Key))
            {
                Raise(batch.ToList());
            }
        }
    }

    private static void Raise(List<WorkReported> batch)
    {
        var count = batch.Count.ToString(CultureInfo.CurrentCulture);
        var (level, message) = UiWorkRow.Worst(batch) switch
        {
            WorkState.Failed => (ToastLevel.Error, RaskStrings.Get(RaskString.WorkBatchFailed, "{0} items ended, some failed.", count)),
            WorkState.Warned => (ToastLevel.Warning,
                RaskStrings.Get(RaskString.WorkBatchWarned, "{0} items are done, some with a warning.", count)),
            WorkState.Stopped => (ToastLevel.Info,
                RaskStrings.Get(RaskString.WorkBatchStopped, "{0} items ended, some were stopped.", count)),
            _ => (ToastLevel.Success, RaskStrings.Get(RaskString.WorkBatchDone, "All {0} items are done.", count)),
        };
        Toast.TryShow(level, message, Core.Live.LiveRenderContext.Current?.Services);
    }

    private static string Ended(WorkReported work) => work.State switch
    {
        WorkState.Done => RaskStrings.Get(RaskString.WorkEndedDone, "{0} is done.", work.Title),
        WorkState.Warned => RaskStrings.Get(RaskString.WorkEndedWarned, "{0} is done, with a warning.", work.Title),
        WorkState.Failed => RaskStrings.Get(RaskString.WorkEndedFailed, "{0} failed.", work.Title),
        _ => RaskStrings.Get(RaskString.WorkEndedStopped, "{0} was stopped.", work.Title),
    };
}
