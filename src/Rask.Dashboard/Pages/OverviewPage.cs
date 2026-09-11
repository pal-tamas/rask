using Rask.Core.Routing;
using Rask.Dashboard.Panels;

namespace Rask.Dashboard.Pages;

/// <summary>
/// The landing panel: one card per queue. Deliberately counters-only — the question it
/// answers is "is anything wrong?", and the answer should be readable without reading.
/// </summary>
[Route("")]
[ParentRoute(typeof(DashboardLayout))]
public sealed partial class OverviewPage(IEnumerable<IQueuePanel> queues, RaskDashboardOptions options) : PollingPanel
{
    private readonly List<(IQueuePanel Panel, QueueCounts Counts)> _queues = [];

    /// <inheritdoc />
    protected override RaskDashboardOptions Options => options;

    /// <inheritdoc />
    protected override async Task<object?> LoadAsync(CancellationToken cancellationToken)
    {
        _queues.Clear();
        foreach (var queue in queues.Where(q => q.IsAvailable).OrderBy(q => q.Title, StringComparer.Ordinal))
        {
            _queues.Add((queue, await queue.CountsAsync(cancellationToken).ConfigureAwait(false)));
        }

        // The comparison key is every number on screen, flattened — a value tuple of a list would compare
        // by reference, so the counts are folded into a string instead.
        return string.Join('|', _queues.Select(q =>
            $"{q.Panel.Slug}:{q.Counts.Due}:{q.Counts.Delayed}:{q.Counts.Failed}:{q.Counts.Processed}"));
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (IsLoading)
        {
            return DashboardLoading;
        }

        if (_queues.Count == 0)
        {
            return UiCard[
                UiEmpty
                    .Heading("No batteries registered")
                    .Detail("Add Rask.Jobs, Rask.Outbox, Rask.Mail or Rask.Cache and map their tables to see them here.")
            ];
        }

        return [
            UiHeader.Heading("Overview").Caption(StateLine()),
            DashboardError.Message(LoadError),
            FailureBanner(),
            UiGrid[_queues.Select(q => QueueCard(q.Panel, q.Counts))],
            DashboardParked.Parked(IsParked).Resume(ResumeAsync),
        ];
    }

    // The one number worth interrupting for. Processed climbing looks healthy while a queue retries itself
    // to death, so the dead-letter total gets its own banner rather than only a tile among tiles.
    private Component? FailureBanner()
    {
        var failed = _queues.Sum(q => q.Counts.Failed);
        if (failed == 0)
        {
            return null;
        }

        var worst = _queues.Where(q => q.Counts.Failed > 0).OrderByDescending(q => q.Counts.Failed).ToList();
        return UiAlert.Tone(UiTone.Error)[
            UiIcon.Name(UiIconName.Warning),
            Span[
                $"{failed} dead letter{(failed == 1 ? "" : "s")} — ",
                string.Join(", ", worst.Select(q => $"{q.Counts.Failed} in {q.Panel.Title.ToLowerInvariant()}")),
                ". These have run out of attempts and will not be retried."
            ]
        ];
    }

    // The one-line state of the whole console, beside the heading — so the first thing on screen says
    // whether anything needs attention before any tile is read.
    private string StateLine()
    {
        var outstanding = _queues.Sum(q => q.Counts.Outstanding);
        var failed = _queues.Sum(q => q.Counts.Failed);
        var queues = _queues.Count == 1 ? "1 queue" : $"{_queues.Count} queues";

        return failed > 0
            ? $"{queues} · {outstanding} outstanding · {failed} failed"
            : $"{queues} · {outstanding} outstanding · nothing failed";
    }

    /// <summary>
    /// One card per queue: what it is, whether it is healthy, and the two numbers worth knowing.
    /// </summary>
    /// <remarks>
    /// This was two tiles per queue, so a deployment running three of them opened on six tiles that were
    /// mostly the word "outstanding" repeated — and, at four to a row, a second row holding two. A queue is
    /// one thing, so it gets one card, and the grid divides evenly by the number of queues rather than by
    /// twice it. The whole card is one link, which is why its corner holds a status rather than a button.
    /// </remarks>
    private Component QueueCard(IQueuePanel panel, QueueCounts counts)
    {
        var failing = counts.Failed > 0;

        return UiCard
            .Key(panel.Slug)
            .Href(Routes.QueuePage(panel.Slug))
            .Icon(panel.Icon)
            .Heading(panel.Title)
            .Action(UiStatusDot
                .Label(failing ? $"{counts.Failed} failed" : "healthy")
                .Tone(failing ? UiTone.Error : UiTone.Success))[
            UiMetricRow.Columns(2)[
                UiMetric
                    .Key("outstanding")
                    .Label("Outstanding")
                    .Value(counts.Outstanding.ToString())
                    .Caption(counts.Delayed > 0 ? $"{counts.Delayed} waiting on a retry" : "nothing waiting"),
                UiMetric
                    .Key("failed")
                    .Label("Failed")
                    .Value(counts.Failed.ToString())
                    .Tone(failing ? UiTone.Error : null)
                    .Caption($"dead after {panel.MaxAttempts} attempts")
            ]
        ];
    }
}
