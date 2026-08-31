using Rask.Core.Routing;
using Rask.Dashboard.Panels;

namespace Rask.Dashboard.Pages;

/// <summary>
/// One queue in detail: the counts as filter tabs, then the rows behind whichever is selected. The same
/// page serves the outbox, jobs and mail — they differ in which columns mean what, not in what an operator
/// needs from them.
/// </summary>
[Route("queues/{queue}")]
[ParentRoute(typeof(DashboardLayout))]
public sealed partial class QueuePage(
    IEnumerable<IQueuePanel> queues,
    RaskDashboardOptions options,
    TimeProvider timeProvider) : PollingPanel
{
    private IQueuePanel? _panel;
    private QueueCounts _counts;
    private IReadOnlyList<QueueRow> _rows = [];
    private int _total;
    private int _page;
    private long? _expanded;
    private string? _message;
    private (string Prompt, Func<CancellationToken, Task<string>> Action)? _pending;

    /// <summary>Which queue, from the route.</summary>
    [RouteParam]
    public string Queue { get; set; } = "";

    /// <summary>Which slice, from the query string, so a filtered view is a shareable link.</summary>
    [QueryParam("show")]
    public string? Show { get; set; }

    /// <inheritdoc />
    protected override RaskDashboardOptions Options => options;

    private QueueFilter Filter =>
        Enum.TryParse<QueueFilter>(Show, ignoreCase: true, out var parsed) ? parsed : QueueFilter.Outstanding;

    /// <inheritdoc />
    protected override async Task<object?> LoadAsync(CancellationToken cancellationToken)
    {
        _panel = queues.FirstOrDefault(q =>
            string.Equals(q.Slug, Queue, StringComparison.OrdinalIgnoreCase) && q.IsAvailable);

        if (_panel is null)
        {
            _rows = [];
            _total = 0;
            return null;
        }

        _counts = await _panel.CountsAsync(cancellationToken).ConfigureAwait(false);
        (_rows, _total) = await _panel
            .PageAsync(Filter, _page * options.PageSize, options.PageSize, cancellationToken)
            .ConfigureAwait(false);

        // Row identity plus attempt count is enough to notice any change that matters: a row moving state
        // changes the filter it appears in, and a retry bumps Attempts.
        return string.Join('|',
            [$"{_counts.Due}:{_counts.Delayed}:{_counts.Failed}:{_counts.Processed}:{_total}",
             .. _rows.Select(r => $"{r.Id}:{r.Attempts}:{r.ProcessedAt?.Ticks ?? 0}")]);
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (IsLoading)
        {
            return DashboardLoading;
        }

        if (_panel is null)
        {
            return DashboardEmpty.Heading($"No queue called \"{Queue}\"")
                .Detail("Either that battery isn't registered, or its table isn't mapped into the DbContext.");
        }

        return [
            OpsHeader
                .Heading(_panel.Title)
                .Icon(_panel.Icon)
                .Actions(Div.Class("flex flex-wrap gap-2")[QueueActionButtons()]),
            DashboardError.Message(LoadError),
            ActionResult(),
            FilterTabs(),
            _rows.Count == 0 ? EmptyForFilter() : RowsTable(),
            Pager(),
            DashboardParked.Parked(IsParked).Resume(ResumeAsync),
        ];
    }

    private Component FilterTabs() =>
        Div.Class("mb-5")[
            OpsTabs[
                Tab(QueueFilter.Outstanding, "Outstanding", _counts.Outstanding, alarm: false),
                Tab(QueueFilter.Due, "Due", _counts.Due, alarm: false),
                Tab(QueueFilter.Delayed, "Delayed", _counts.Delayed, alarm: false),
                Tab(QueueFilter.Failed, "Failed", _counts.Failed, alarm: _counts.Failed > 0),
                Tab(QueueFilter.Processed, "Processed", _counts.Processed, alarm: false)
            ]
        ];

    private Component Tab(QueueFilter filter, string label, int count, bool alarm) =>
        OpsTab
            .Key(label)
            .Href(Routes.QueuePage(_panel!.Slug, Show: filter.ToString().ToLowerInvariant()))
            .Label(label)
            .Active(Filter == filter)
            .Count(count.ToString())
            .Alarm(alarm);

    private Component EmptyForFilter() => DashboardEmpty.Heading($"Nothing {Filter.ToString().ToLowerInvariant()}")
        .Detail(Filter == QueueFilter.Failed
            ? "No dead letters. This is the number you want at zero."
            : "Nothing in this slice right now.");

    private Component RowsTable()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return OpsTable[
            Thead.Class("border-b border-ops-line text-xs text-ops-muted")[
                Tr[
                    Th.Class("px-3 py-2 font-medium")["#"],
                    Th.Class("px-3 py-2 font-medium")[TypeColumnLabel()],
                    Th.Class("px-3 py-2 font-medium")["When"],
                    Th.Class("px-3 py-2 font-medium")["Attempts"],
                    Th.Class("px-3 py-2 font-medium")["Status"],
                    Th.Class("px-3 py-2")
                ]
            ],
            Tbody[_rows.SelectMany(r => Row(r, now))]
        ];
    }

    // Mail's "type" column is really its subject; calling it Type on that page would be a small lie.
    private string TypeColumnLabel() => _panel!.Slug == "mail" ? "Subject" : "Type";

    private IEnumerable<Component> Row(QueueRow row, DateTime now)
    {
        var isDead = row.ProcessedAt is null && row.Attempts >= _panel!.MaxAttempts;
        yield return Tr.Key(row.Id).Class(isDead
            ? "border-b border-ops-line/60 bg-red-500/5 last:border-0"
            : "border-b border-ops-line/60 last:border-0")[
            Td.Class($"px-3 py-2 text-ops-muted {Ops.Mono}")[row.Id.ToString()],
            Td.Class("px-3 py-2")[
                Div.Class("max-w-[28rem] truncate").Title(row.Type)[row.Type]
            ],
            Td.Class("px-3 py-2 text-xs text-ops-muted").Title(row.CreatedAt.ToString("u"))[
                DashboardParts.Ago(row.CreatedAt, now)
            ],
            Td.Class("px-3 py-2 tabular-nums")[row.Attempts.ToString()],
            Td.Class("px-3 py-2")[StatusBadge(row, isDead, now)],
            Td.Class("px-3 py-2 text-right")[
                Div.Class("flex justify-end gap-1")[RowButtons(row, isDead)]
            ]
        ];

        if (_expanded == row.Id)
        {
            yield return Tr.Key($"{row.Id}-details")[
                Td.Colspan(6).Class("border-b border-ops-line/60 bg-black/20 px-3 py-3")[Details(row)]
            ];
        }
    }

    private IEnumerable<Component> RowButtons(QueueRow row, bool isDead)
    {
        foreach (var button in RowActionButtons(row, isDead))
        {
            yield return button;
        }

        yield return Button
            .Key("details")
            .Type("button")
            .Class(Ops.Button)
            .OnClick(() => Toggle(row.Id))[_expanded == row.Id ? "Hide" : "Details"];
    }

    private Component StatusBadge(QueueRow row, bool isDead, DateTime now) => row switch
    {
        { ProcessedAt: not null } => OpsBadge.Label("done").Tone("ok"),
        _ when isDead => OpsBadge.Label("dead letter").Tone("danger"),
        _ when row.RunAt > now =>
            OpsBadge.Label($"retries in {DashboardParts.Duration(row.RunAt - now)}"),
        _ => OpsBadge.Label("due").Tone("info"),
    };

    private static new Component Details(QueueRow row) =>
        Div.Class("text-xs")[
            row.Error is { } error
                ? Div.Class("mb-3")[
                    Div.Class("mb-1 font-medium text-red-300")["Last error"],
                    Pre.Class($"whitespace-pre-wrap break-all text-ops-muted {Ops.Mono}")[error]
                ]
                : null,
            Div.Class("mb-1 font-medium text-ops-muted")["Payload"],
            Pre.Class($"whitespace-pre-wrap break-all text-ops-muted {Ops.Mono}")[row.Payload]
        ];

    private void Toggle(long id)
    {
        _expanded = _expanded == id ? null : id;
        StateHasChanged();
    }

    // ── Actions ─────────────────────────────────────────────────────────────────────────────────────
    // Every button is hidden, not merely disabled, when its tier is off: an operator shouldn't have to
    // discover by clicking that the deployment doesn't allow something.

    private IEnumerable<Component> QueueActionButtons()
    {
        if (!options.Actions.HasFlag(RaskDashboardActions.Safe))
        {
            yield break;
        }

        if (_counts.Failed > 0)
        {
            yield return Button
                .Key("retry-all")
                .Type("button")
                .Class(Ops.Danger)
                .OnClickAsync(() => RunAsync(
                    $"Retry all {_counts.Failed} dead letters?",
                    async ct => $"Re-queued {await _panel!.RetryAllAsync(ct).ConfigureAwait(false)}."))[
                OpsIcon.Name(OpsIconName.Retry).Class("size-4"),
                Span["Retry all failed"]
            ];
        }

        if (_counts.Processed > 0)
        {
            yield return Button
                .Key("purge")
                .Type("button")
                .Class(Ops.Button)
                .OnClickAsync(() => RunAsync(
                    "Delete processed rows older than 7 days? Outstanding work and dead letters are kept.",
                    async ct => $"Purged {await _panel!.PurgeProcessedAsync(TimeSpan.FromDays(7), ct).ConfigureAwait(false)}."))[
                "Purge processed"
            ];
        }
    }

    private IEnumerable<Component> RowActionButtons(QueueRow row, bool isDead)
    {
        if (isDead && options.Actions.HasFlag(RaskDashboardActions.Safe))
        {
            yield return Button
                .Key("retry")
                .Type("button")
                .Class(Ops.Danger)
                .OnClickAsync(() => RunAsync(
                    null,   // retrying one dead letter is reversible enough not to need a confirmation
                    async ct => await _panel!.RetryAsync(row.Id, ct).ConfigureAwait(false) > 0
                        ? $"Re-queued #{row.Id}."
                        : $"#{row.Id} was already picked up."))["Retry"];
        }

        if (row.ProcessedAt is null && options.Actions.HasFlag(RaskDashboardActions.Destructive))
        {
            yield return Button
                .Key("delete")
                .Type("button")
                .Class(Ops.Danger)
                .OnClickAsync(() => RunAsync(
                    $"Delete #{row.Id}? The work is discarded and cannot be recovered.",
                    async ct => await _panel!.DeleteAsync(row.Id, ct).ConfigureAwait(false) > 0
                        ? $"Deleted #{row.Id}."
                        : $"#{row.Id} had already completed and was left alone."))["Delete"];
        }
    }

    // Confirmation is a state flip rather than a JS dialog: the prompt renders as an alert with the
    // pending action attached, so it works on the Server transport with no client script.
    private Task RunAsync(string? confirm, Func<CancellationToken, Task<string>> action)
    {
        if (confirm is not null)
        {
            _pending = (confirm, action);
            StateHasChanged();
            return Task.CompletedTask;
        }

        return ExecuteAsync(action);
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task<string>> action)
    {
        _pending = null;
        try
        {
            _message = await action(CancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failed action must report itself, not tear the page down.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _message = $"Failed: {ex.Message}";
        }

        _page = 0;
        _expanded = null;
        await LoadAsync(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }

    private Component? ActionResult()
    {
        if (_pending is { } pending)
        {
            return OpsNotice.Tone("warn")[
                Span.Class("grow")[pending.Prompt],
                Button.Type("button").Class(Ops.Danger)
                    .OnClickAsync(() => ExecuteAsync(pending.Action))["Confirm"],
                Button.Type("button").Class(Ops.Button).OnClick(Cancel)["Cancel"]
            ];
        }

        return _message is { } message
            ? OpsNotice.Tone("info")[
                Span.Class("grow")[message],
                Button.Type("button")
                    .Class(Ops.Quiet)
                    .Aria(new Dictionary<string, string?> { ["label"] = "Dismiss" })
                    .OnClick(Dismiss)["Dismiss"]
            ]
            : null;
    }

    private void Cancel()
    {
        _pending = null;
        StateHasChanged();
    }

    private void Dismiss()
    {
        _message = null;
        StateHasChanged();
    }

    private Component? Pager()
    {
        var pages = (int)Math.Ceiling(_total / (double)options.PageSize);
        if (pages <= 1)
        {
            return null;
        }

        return Div.Class("mt-4 flex items-center gap-3")[
            Button.Type("button").Class(Ops.Button)
                .Disabled(_page == 0)
                .OnClickAsync(() => GoAsync(_page - 1))["Previous"],
            Span.Class("text-xs text-ops-muted")[$"Page {_page + 1} of {pages} — {_total} rows"],
            Button.Type("button").Class(Ops.Button)
                .Disabled(_page >= pages - 1)
                .OnClickAsync(() => GoAsync(_page + 1))["Next"]
        ];
    }

    private async Task GoAsync(int page)
    {
        _page = Math.Max(0, page);
        _expanded = null;
        await LoadAsync(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }
}
