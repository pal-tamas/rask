using Rask.Background;
using Rask.Core.Globalization;

namespace Rask;

/// <summary>
///     What a <see cref="UiWorkNotices" /> or a <see cref="UiWorkList" /> says to a screen reader when a job it has seen running
///     ends — "Import of prices.csv is done." — in a polite live region, so it waits for the reader to finish what it reads.
/// </summary>
/// <remarks>
///     Only an end it saw coming is said: a page opened after a job ended shows it ended, and says nothing.
/// </remarks>
[Rask.Core.RaskMarkup]
internal sealed partial class UiWorkAnnouncer
{
    private readonly HashSet<long> _running = [];

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
    }

    /// <summary>The live region: always on the page, so a reader hears the words it takes.</summary>
    internal Component Region() =>
        Div.Class("sr-only").Role("status").Aria("live", "polite").Attributes(("data-ui-work-said", ""))[Said];

    private static string Ended(WorkReported work) => work.State switch
    {
        WorkState.Done => RaskStrings.Get(RaskString.WorkEndedDone, "{0} is done.", work.Title),
        WorkState.Failed => RaskStrings.Get(RaskString.WorkEndedFailed, "{0} failed.", work.Title),
        _ => RaskStrings.Get(RaskString.WorkEndedStopped, "{0} was stopped.", work.Title),
    };
}
