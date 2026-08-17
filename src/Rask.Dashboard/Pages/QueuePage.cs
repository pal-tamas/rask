using Rask.Core.Routing;
using Rask.Dashboard.Panels;

namespace Rask.Dashboard.Pages;

/// <summary>
/// One queue in detail: the counts as filter tabs, then the rows behind whichever is selected. The same
/// page serves the outbox, jobs and mail — they differ in which columns mean what, not in what an operator
/// needs from them.
/// </summary>
public sealed partial class QueuePage(
    IEnumerable<IQueuePanel> queues,
    RaskDashboardOptions options,
    TimeProvider timeProvider) : PollingPanel
{
    protected override string Route => "queues/{queue}";

    protected override Type? Parent => typeof(DashboardLayout);


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

    protected override RaskDashboardOptions Options => options;

    private new QueueFilter Filter =>
        Enum.TryParse<QueueFilter>(Show, ignoreCase: true, out var parsed) ? parsed : QueueFilter.Outstanding;

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
            Div.Class("d-flex align-items-center gap-2 mb-3")[
                BsIcon.Name(_panel.Icon).Class("fs-4"),
                H1.Class("h4 mb-0")[_panel.Title],
                Div.Class("ms-auto d-flex gap-2")[QueueActionButtons()]
            ],
            DashboardError.Message(LoadError),
            ActionResult(),
            FilterTabs(),
            _rows.Count == 0 ? EmptyForFilter() : RowsTable(),
            Pager(),
            DashboardParked.Parked(IsParked).Resume(ResumeAsync),
        ];
    }

    private Component FilterTabs() =>
        BsNav.Class("nav-pills gap-1 mb-3")[
            Tab(QueueFilter.Outstanding, "Outstanding", _counts.Outstanding, null),
            Tab(QueueFilter.Due, "Due", _counts.Due, null),
            Tab(QueueFilter.Delayed, "Delayed", _counts.Delayed, null),
            Tab(QueueFilter.Failed, "Failed", _counts.Failed, _counts.Failed > 0 ? BsColor.Danger : null),
            Tab(QueueFilter.Processed, "Processed", _counts.Processed, null)
        ];

    private Component Tab(QueueFilter filter, string label, int count, BsColor? tone) =>
        BsNavItem[
            BsLink
                .Href(Routes.QueuePage(_panel!.Slug, Show: filter.ToString().ToLowerInvariant()))
                .Class(Bs.Join("nav-link d-flex align-items-center gap-2", Filter == filter ? "active" : null))[
                Span[label],
                BsBadge.Color(tone ?? (Filter == filter ? BsColor.Light : BsColor.Secondary)).Pill(true)[count.ToString()]
            ]
        ];

    private Component EmptyForFilter() => DashboardEmpty.Heading($"Nothing {Filter.ToString().ToLowerInvariant()}")
        .Detail(Filter == QueueFilter.Failed
            ? "No dead letters. This is the number you want at zero."
            : "Nothing in this slice right now.");

    private Component RowsTable()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return BsTable.Small(true).Hover(true).Responsive(true)[
            Thead[Tr[
                Th["#"], Th[TypeColumnLabel()], Th["When"], Th["Attempts"], Th["Status"], Th
            ]],
            Tbody[_rows.SelectMany(r => Row(r, now))]
        ];
    }

    // Mail's "type" column is really its subject; calling it Type on that page would be a small lie.
    private string TypeColumnLabel() => _panel!.Slug == "mail" ? "Subject" : "Type";

    private IEnumerable<Component> Row(QueueRow row, DateTime now)
    {
        var isDead = row.ProcessedAt is null && row.Attempts >= _panel!.MaxAttempts;
        yield return Tr.Key(row.Id).Class(isDead ? "table-danger" : null)[
            Td.Class("text-body-secondary")[row.Id.ToString()],
            Td[Div.Class("text-truncate").Style("max-width:28rem").Title(row.Type)[row.Type]],
            Td.Title(row.CreatedAt.ToString("u"))[DashboardParts.Ago(row.CreatedAt, now)],
            Td[row.Attempts.ToString()],
            Td[StatusBadge(row, isDead, now)],
            Td.Class("text-end")[
                Div.Class("d-flex gap-1 justify-content-end")[RowButtons(row, isDead)]
            ]
        ];

        if (_expanded == row.Id)
        {
            yield return Tr.Key($"{row.Id}-details")[Td.Colspan(6).Class("bg-body-tertiary")[Details(row)]];
        }
    }

    private IEnumerable<Component> RowButtons(QueueRow row, bool isDead)
    {
        foreach (var button in RowActionButtons(row, isDead))
        {
            yield return button;
        }

        yield return BsButton
            .Key("details")
            .Color(BsColor.Secondary)
            .Outline(true)
            .Size(BsSize.Sm)
            .OnClick(() => Toggle(row.Id))[_expanded == row.Id ? "Hide" : "Details"];
    }

    private Component StatusBadge(QueueRow row, bool isDead, DateTime now) => row switch
    {
        { ProcessedAt: not null } => BsBadge.Color(BsColor.Success)["done"],
        _ when isDead => BsBadge.Color(BsColor.Danger)["dead letter"],
        _ when row.RunAt > now => BsBadge.Color(BsColor.Secondary)[$"retries in {DashboardParts.Duration(row.RunAt - now)}"],
        _ => BsBadge.Color(BsColor.Info)["due"],
    };

    private static new Component Details(QueueRow row) =>
        Div.Class("small")[
            row.Error is { } error
                ? Div.Class("mb-2")[
                    Div.Class("fw-semibold text-danger")["Last error"],
                    Pre.Class("mb-0 text-body-secondary text-wrap")[error]
                ]
                : null,
            Div.Class("fw-semibold")["Payload"],
            Pre.Class("mb-0 text-body-secondary text-wrap")[row.Payload]
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
            yield return BsButton
                .Key("retry-all")
                .Color(BsColor.Danger)
                .Size(BsSize.Sm)
                .OnClickAsync(() => RunAsync(
                    $"Retry all {_counts.Failed} dead letters?",
                    async ct => $"Re-queued {await _panel!.RetryAllAsync(ct).ConfigureAwait(false)}."))[
                BsIcon.Name(BsIconName.ArrowRepeat),
                Span.Class("ms-1")["Retry all failed"]
            ];
        }

        if (_counts.Processed > 0)
        {
            yield return BsButton
                .Key("purge")
                .Color(BsColor.Secondary)
                .Outline(true)
                .Size(BsSize.Sm)
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
            yield return BsButton
                .Key("retry")
                .Color(BsColor.Danger)
                .Outline(true)
                .Size(BsSize.Sm)
                .OnClickAsync(() => RunAsync(
                    null,   // retrying one dead letter is reversible enough not to need a confirmation
                    async ct => await _panel!.RetryAsync(row.Id, ct).ConfigureAwait(false) > 0
                        ? $"Re-queued #{row.Id}."
                        : $"#{row.Id} was already picked up."))["Retry"];
        }

        if (row.ProcessedAt is null && options.Actions.HasFlag(RaskDashboardActions.Destructive))
        {
            yield return BsButton
                .Key("delete")
                .Color(BsColor.Danger)
                .Outline(true)
                .Size(BsSize.Sm)
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
            return BsAlert.Color(BsColor.Warning).Class("d-flex align-items-center gap-2")[
                Span.Class("flex-grow-1")[pending.Prompt],
                BsButton.Color(BsColor.Danger).Size(BsSize.Sm).OnClickAsync(() => ExecuteAsync(pending.Action))["Confirm"],
                BsButton.Color(BsColor.Secondary).Outline(true).Size(BsSize.Sm).OnClick(Cancel)["Cancel"]
            ];
        }

        return _message is { } message
            ? BsAlert.Color(BsColor.Info).Class("d-flex align-items-center gap-2")[
                Span.Class("flex-grow-1")[message],
                BsCloseButton.OnClick(Dismiss)
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

        return Div.Class("d-flex align-items-center gap-2")[
            BsButton
                .Color(BsColor.Secondary)
                .Outline(true)
                .Size(BsSize.Sm)
                .Disabled(_page == 0)
                .OnClickAsync(() => GoAsync(_page - 1))["Previous"],
            Span.Class("small text-body-secondary")[$"Page {_page + 1} of {pages} — {_total} rows"],
            BsButton
                .Color(BsColor.Secondary)
                .Outline(true)
                .Size(BsSize.Sm)
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
