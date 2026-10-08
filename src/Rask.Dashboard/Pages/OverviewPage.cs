using System.Globalization;
using Rask.Core.Routing;
using Rask.Dashboard.Panels;

namespace Rask.Dashboard.Pages;

/// <summary>
/// The landing panel: one card per queue. Deliberately counters-only — the question it
/// answers is "is anything wrong?", and the answer should be readable without reading.
/// </summary>
[Route("")]
[ParentRoute(typeof(DashboardLayout))]
public sealed partial class OverviewPage(IEnumerable<IQueuePanel> queues, OpsOptions options) : PollingPanel
{
    private readonly List<(IQueuePanel Panel, QueueCounts Counts)> _queues = [];

    /// <inheritdoc />
    protected override OpsOptions Options => options;

    /// <inheritdoc />
    protected override async Task<object?> Load(CancellationToken cancellationToken)
    {
        _queues.Clear();
        foreach (var queue in queues.Where(q => q.IsAvailable).OrderBy(q => q.Title, StringComparer.Ordinal))
        {
            _queues.Add((queue, await queue.Counts(cancellationToken).ConfigureAwait(false)));
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
            return Ui.Card[
                Ui.Empty
                    .Title("No batteries registered")
                    .Detail("Add Rask.Jobs, Rask.Outbox, Rask.Mail or Rask.Cache and map their tables to see them here.")
            ];
        }

        return [
            DashboardHeading.Title("Overview").Caption(StateLine()),
            DashboardError.Message(LoadError),
            FailureBanner(),
            Ui.Grid[_queues.Select(q => QueueCard(q.Panel, q.Counts))],
            DashboardParked.Parked(IsParked).Resume(Resume),
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
        return Ui.Callout.Danger.Icon(Ui.IconName.ExclamationTriangle).Role("alert")[
            Ui.CalloutHeading[
                $"{failed} dead letter{(failed == 1 ? "" : "s")} — ",
                string.Join(", ", worst.Select(q => $"{q.Counts.Failed} in {q.Panel.Title.ToLowerInvariant()}")),
                "."
            ],
            Ui.CalloutText["These have run out of attempts and will not be retried."]
        ];
    }

    // The one-line state of the whole console, beside the heading — so the first thing on screen says
    // whether anything needs attention before any tile is read.
    private string StateLine()
    {
        var outstanding = _queues.Sum(q => q.Counts.Outstanding);
        var failed = _queues.Sum(q => q.Counts.Failed);
        var queueCount = _queues.Count == 1 ? "1 queue" : $"{_queues.Count} queues";

        return failed > 0
            ? $"{queueCount} · {outstanding} outstanding · {failed} failed"
            : $"{queueCount} · {outstanding} outstanding · nothing failed";
    }

    /// <summary>
    /// One card per queue: what it is, whether it is healthy, and the two numbers worth knowing.
    /// </summary>
    /// <remarks>
    /// This was two tiles per queue, so a deployment running three of them opened on six tiles that were
    /// mostly the word "outstanding" repeated — and, at four to a row, a second row holding two. A queue is
    /// one thing, so it gets one card, and the grid divides evenly by the number of queues rather than by
    /// twice it. The whole card sits in one link, which is why it holds a status and never a button.
    /// </remarks>
    private static Component QueueCard(IQueuePanel panel, QueueCounts counts)
    {
        var failing = counts.Failed > 0;

        // Flux's link card: the link is around the card, not a prop of it.
        return NavLink.Key(panel.Slug).Href(Routes.QueuePage(panel.Slug))[Ui.Card[
            Ui.CardHeader[
                Ui.CardHeading.Level(2)[panel.Title],
                Ui.CardSubheading[Ui.StatusDot
                    .Label(failing ? $"{counts.Failed} failed" : "healthy")
                    .Tone(failing ? Ui.Tone.Error : Ui.Tone.Success)]
            ],
            Ui.CardBody[Ui.MetricRow.Columns(2)[
                Ui.Metric
                    .Key("outstanding")
                    .Label("Outstanding")
                    .Value(counts.Outstanding.ToString(CultureInfo.CurrentCulture))
                    .Caption(counts.Delayed > 0 ? $"{counts.Delayed} waiting on a retry" : "nothing waiting"),
                Ui.Metric
                    .Key("failed")
                    .Label("Failed")
                    .Value(counts.Failed.ToString(CultureInfo.CurrentCulture))
                    .Tone(failing ? Ui.Tone.Error : null)
                    .Caption($"dead after {panel.MaxAttempts} attempts")
            ]]
        ]];
    }
}
