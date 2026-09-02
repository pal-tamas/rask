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
            return DashboardEmpty.Heading("No batteries registered")
                .Detail("Add Rask.Jobs, Rask.Outbox, Rask.Mail or Rask.Cache and map their tables to see them here.");
        }

        return [
            OpsHeader.Heading("Overview").Caption(StateLine()),
            DashboardError.Message(LoadError),
            FailureBanner(),
            OpsGrid[_queues.Select(q => QueueCard(q.Panel, q.Counts))],
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
        return Div.Role("alert")
            .Class(
                "mb-4 flex items-start gap-3 rounded-xl border border-ui-danger/30 bg-ui-danger/5 px-4 py-3 "
                + "text-sm text-ui-danger sm:mb-6")[
            UiIcon.Name(UiIconName.Warning).Class("mt-0.5 size-5 shrink-0"),
            Span.Class("min-w-0 break-words")[
                $"{failed} dead letter{(failed == 1 ? "" : "s")} — ",
                Span[string.Join(", ", worst.Select(q => $"{q.Counts.Failed} in {q.Panel.Title.ToLowerInvariant()}"))],
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
    /// twice it.
    /// </remarks>
    private Component QueueCard(IQueuePanel panel, QueueCounts counts)
    {
        var failing = counts.Failed > 0;

        return NavLink
            .Key(panel.Slug)
            .Href(Routes.QueuePage(panel.Slug))
            .Class($"{Ops.Card} block no-underline transition-colors hover:bg-ui-well")[
            Div.Class("flex items-center gap-2")[
                UiIcon.Name(panel.Icon).Class("size-5 shrink-0 text-ui-muted"),
                Span.Class("truncate font-medium text-ui-ink")[panel.Title],
                Div.Class("ml-auto shrink-0")[
                    UiStatusDot
                        .Label(failing ? $"{counts.Failed} failed" : "healthy")
                        .Tone(failing ? UiTone.Danger : UiTone.Ok)
                ]
            ],
            Div.Class("mt-4 flex items-baseline gap-6")[
                Figure("Outstanding", counts.Outstanding, tone: null),
                Figure("Failed", counts.Failed, tone: failing ? "danger" : null)
            ],
            Div.Class("mt-2 truncate text-xs text-ui-muted")[
                counts.Delayed > 0
                    ? $"{counts.Delayed} waiting on a retry · dead after {panel.MaxAttempts} attempts"
                    : $"nothing waiting · dead after {panel.MaxAttempts} attempts"
            ]
        ];
    }

    // Named Figure, not Stat or Metric: both of those are chain entries on this markup host.
    private Component Figure(string label, int value, string? tone) =>
        Div[
            Div.Class("text-xs font-medium text-ui-muted")[label],
            Div.Class("mt-0.5 text-2xl font-semibold tabular-nums tracking-tight "
                      + (tone == "danger" ? "text-ui-danger" : "text-ui-ink"))[
                value.ToString()
            ]
        ];
}
