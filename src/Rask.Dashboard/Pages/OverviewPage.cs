using Rask.Core.Routing;
using Rask.Dashboard.Panels;

namespace Rask.Dashboard.Pages;

/// <summary>
/// The landing panel: one tile per queue, plus the cache. Deliberately counters-only — the question it
/// answers is "is anything wrong?", and the answer should be readable without reading.
/// </summary>
[Route("")]
[ParentRoute(typeof(DashboardLayout))]
public sealed partial class OverviewPage(IEnumerable<IQueuePanel> queues, RaskDashboardOptions options) : PollingPanel
{
    private readonly List<(IQueuePanel Panel, QueueCounts Counts)> _queues = [];

    protected override RaskDashboardOptions Options => options;

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

    protected override Component? Render()
    {
        if (IsLoading)
        {
            return DashboardParts.Loading();
        }

        if (_queues.Count == 0)
        {
            return DashboardParts.Empty(
                "No batteries registered",
                "Add Rask.Jobs, Rask.Outbox, Rask.Mail or Rask.Cache and map their tables to see them here.");
        }

        return [
            DashboardParts.Error(LoadError),
            FailureBanner(),
            BsRow(Class: "g-3")[_queues.SelectMany(q => Tiles(q.Panel, q.Counts))],
            DashboardParts.Parked(IsParked, ResumeAsync),
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
        return BsAlert(Color: BsColor.Danger, Class: "d-flex align-items-center gap-2")[
            BsIcon(Name: BsIconName.ExclamationTriangle),
            Span()[
                $"{failed} dead letter{(failed == 1 ? "" : "s")} — ",
                Span()[string.Join(", ", worst.Select(q => $"{q.Counts.Failed} in {q.Panel.Title.ToLowerInvariant()}"))],
                ". These have run out of attempts and will not be retried."
            ]
        ];
    }

    private IEnumerable<Component> Tiles(IQueuePanel panel, QueueCounts counts)
    {
        yield return BsCol(Sm: 6, Lg: 3)[
            BsStat(
                Value: counts.Outstanding.ToString(),
                Label: $"{panel.Title} outstanding",
                Icon: panel.Icon,
                Caption: counts.Delayed > 0 ? $"{counts.Delayed} waiting on a retry" : "nothing waiting",
                Href: Routes.QueuePage(panel.Slug))
        ];

        yield return BsCol(Sm: 6, Lg: 3)[
            BsStat(
                Value: counts.Failed.ToString(),
                Label: $"{panel.Title} failed",
                Icon: BsIconName.ExclamationTriangle,
                Tone: counts.Failed > 0 ? BsColor.Danger : null,
                Caption: $"after {panel.MaxAttempts} attempts",
                Href: Routes.QueuePage(panel.Slug))
        ];
    }
}
