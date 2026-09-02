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
            UiHeader
                .Heading(_panel.Title)
                .Icon(_panel.Icon)
                .Actions(Div.Class("flex flex-wrap gap-2")[QueueActionButtons()]),
            DashboardError.Message(LoadError),
            // A question stays in the flow: it has to be answered before anything else means anything, and
            // a toast is the wrong shape for something you must respond to.
            ConfirmPrompt(),
            CountTiles(),
            _rows.Count == 0 ? EmptyForFilter() : RowsTable(),
            Pager(),
            DashboardParked.Parked(IsParked).Resume(ResumeAsync),
            DetailSheet(),
            ResultToast(),
        ];
    }

    /// <summary>
    /// The counts, as the control that selects which of them you are looking at.
    /// </summary>
    /// <remarks>
    /// These were a tile row and a tab strip carrying the same five numbers. One row that both reports and
    /// filters is fewer things on the screen and one fewer place for the two to disagree — and because each
    /// tile is a real link carrying <c>?show=</c>, the selection is still shareable and keyboard-reachable.
    /// </remarks>
    private Component CountTiles() =>
        Div.Class("mb-4 sm:mb-5")[
            UiMetricRow.Columns(5)[
                Tile(QueueFilter.Outstanding, "Outstanding", _counts.Outstanding, tone: null),
                Tile(QueueFilter.Due, "Due", _counts.Due, tone: null),
                Tile(QueueFilter.Delayed, "Delayed", _counts.Delayed, tone: null),
                Tile(QueueFilter.Failed, "Failed", _counts.Failed, tone: _counts.Failed > 0 ? UiTone.Danger : null),
                Tile(QueueFilter.Processed, "Processed", _counts.Processed, tone: null)
            ]
        ];

    private Component Tile(QueueFilter filter, string label, int count, UiTone? tone) =>
        UiMetric
            .Key(label)
            .Label(label)
            .Value(count.ToString())
            .Tone(tone)
            .Href(Routes.QueuePage(_panel!.Slug, Show: filter.ToString().ToLowerInvariant()))
            .Active(Filter == filter);

    private Component EmptyForFilter() => DashboardEmpty.Heading($"Nothing {Filter.ToString().ToLowerInvariant()}")
        .Detail(Filter == QueueFilter.Failed
            ? "No dead letters. This is the number you want at zero."
            : "Nothing in this slice right now.");

    private Component RowsTable()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return UiTable[
            // The secondary columns are dropped below sm rather than scrolled to. A table an operator has to
            // swipe sideways has hidden the column they came for; the primary cell carries the same facts
            // stacked underneath instead, so nothing is lost — see the remarks on UiTable.
            Thead.Class("border-b border-ui-line text-xs text-ui-muted")[
                Tr[
                    Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["#"],
                    Th.Class("px-3 py-2 font-medium")[TypeColumnLabel()],
                    Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["When"],
                    Th.Class("hidden px-3 py-2 font-medium md:table-cell")["Attempts"],
                    Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["Status"],
                    Th.Class("px-3 py-2")
                ]
            ],
            Tbody[_rows.Select(r => Row(r, now))]
        ];
    }

    // Mail's "type" column is really its subject; calling it Type on that page would be a small lie.
    private string TypeColumnLabel() => _panel!.Slug == "mail" ? "Subject" : "Type";

    private Component Row(QueueRow row, DateTime now)
    {
        var isDead = row.ProcessedAt is null && row.Attempts >= _panel!.MaxAttempts;

        return Tr.Key(row.Id).Class(isDead
            ? "border-b border-ui-line/60 bg-ui-danger/5 last:border-0"
            : "border-b border-ui-line/60 last:border-0")[
            Td.Class($"hidden whitespace-nowrap px-3 py-2 align-top text-ui-muted sm:table-cell {UiStyles.Mono}")[row.Id.ToString()],
            Td.Class("w-full max-w-0 px-3 py-2 align-top")[
                Div.Class("min-w-0")[
                    Div.Class("truncate sm:max-w-[28rem]").Title(row.Type)[row.Type],
                    // What the hidden columns were carrying, folded under the one cell that survives.
                    Div.Class("mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-ui-muted sm:hidden")[
                        Span.Class(UiStyles.Mono)[$"#{row.Id}"],
                        Span.Title(row.CreatedAt.ToString("u"))[DashboardParts.Ago(row.CreatedAt, now)],
                        StatusBadge(row, isDead, now)
                    ]
                ]
            ],
            Td.Class("hidden whitespace-nowrap px-3 py-2 align-top text-xs text-ui-muted sm:table-cell")
                .Title(row.CreatedAt.ToString("u"))[
                DashboardParts.Ago(row.CreatedAt, now)
            ],
            Td.Class("hidden whitespace-nowrap px-3 py-2 align-top tabular-nums md:table-cell")[row.Attempts.ToString()],
            Td.Class("hidden whitespace-nowrap px-3 py-2 align-top sm:table-cell")[StatusBadge(row, isDead, now)],
            Td.Class("px-3 py-2 align-top text-right")[
                Div.Class("flex justify-end gap-1")[RowButtons(row, isDead)]
            ]
        ];
    }

    private IEnumerable<Component> RowButtons(QueueRow row, bool isDead)
    {
        foreach (var button in RowActionButtons(row, isDead))
        {
            yield return button;
        }

        // Opens the detail sheet. A button rather than a clickable row: a <tr> is not focusable, and the
        // console has no script to make one behave like a control.
        yield return UiButton
            .Key("details")
            .Label("Details")
            .Icon(UiIconName.ChevronRight)
            .OnClick(() => Open(row.Id));
    }

    private Component StatusBadge(QueueRow row, bool isDead, DateTime now) => row switch
    {
        { ProcessedAt: not null } => UiBadge.Label("done").Tone("ok"),
        _ when isDead => UiBadge.Label("dead letter").Tone("danger"),
        _ when row.RunAt > now =>
            UiBadge.Label($"retries in {DashboardParts.Duration(row.RunAt - now)}"),
        _ => UiBadge.Label("due").Tone("info"),
    };

    /// <summary>
    /// Everything known about one row, over the list it came from.
    /// </summary>
    /// <remarks>
    /// This was an extra <c>&lt;tr&gt;</c> spliced under the row. A stack trace inside a table cell has to
    /// share the table's column widths, so it was permanently cramped on a desk and unreadable on a phone —
    /// and expanding it pushed every row below it down, which on a polling page moved rows under the
    /// operator's pointer. A sheet is the same information with neither problem.
    /// </remarks>
    private Component? DetailSheet()
    {
        if (_expanded is not { } id || _rows.FirstOrDefault(r => r.Id == id) is not { } row)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var isDead = row.ProcessedAt is null && row.Attempts >= _panel!.MaxAttempts;

        return UiModal
            .Heading(row.Type)
            .Close(Close)
            .Footer(Div.Class("flex flex-wrap gap-2 sm:justify-end")[
                RowActionButtons(row, isDead),
                UiButton.Key("close").Label("Close").OnClick(Close)
            ])[
            UiDetailList[
                UiDetailRow.Key("id").Label("ID").Value($"#{row.Id}").Mono(true),
                UiDetailRow.Key("queue").Label("Queue").Value(_panel!.Title),
                UiDetailRow.Key("attempts").Label("Total attempts")
                    .Value($"{row.Attempts} of {_panel.MaxAttempts}").Mono(true),
                UiDetailRow.Key("created").Label("Queued time").Value(row.CreatedAt.ToString("u")).Mono(true),
                UiDetailRow.Key("runat").Label(row.ProcessedAt is null ? "Runs at" : "Started")
                    .Value(row.RunAt.ToString("u")).Mono(true),
                row.ProcessedAt is { } done
                    ? UiDetailRow.Key("done").Label("Processed").Value(done.ToString("u")).Mono(true)
                    : null,
                UiDetailRow.Key("age").Label("Age").Value(DashboardParts.Ago(row.CreatedAt, now))
            ],
            Div.Class("mt-4 flex items-center gap-2")[
                Span.Class("text-xs font-medium text-ui-muted")["Status"],
                StatusBadge(row, isDead, now)
            ],
            row.Error is { } error
                ? Div.Class("mt-4")[
                    Div.Class("mb-1.5 text-xs font-medium text-ui-danger")["Last error"],
                    UiCode.Content(error).Tone(UiTone.Danger)
                ]
                : null,
            Div.Class("mt-4")[
                Div.Class("mb-1.5 text-xs font-medium text-ui-muted")["Payload"],
                UiCode.Content(row.Payload)
            ]
        ];
    }

    private void Open(long id)
    {
        _expanded = id;
        StateHasChanged();
    }

    private void Close()
    {
        _expanded = null;
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
                .Class(UiStyles.Danger)
                .OnClickAsync(() => RunAsync(
                    $"Retry all {_counts.Failed} dead letters?",
                    async ct => $"Re-queued {await _panel!.RetryAllAsync(ct).ConfigureAwait(false)}."))[
                UiIcon.Name(UiIconName.Retry).Class("size-4"),
                Span["Retry all failed"]
            ];
        }

        if (_counts.Processed > 0)
        {
            yield return Button
                .Key("purge")
                .Type("button")
                .Class(UiStyles.Button)
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
                .Class(UiStyles.Danger)
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
                .Class(UiStyles.Danger)
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

            // Close the sheet, or the question is invisible. The prompt renders in the page's normal flow
            // while the sheet is `fixed inset-0 z-50` over a backdrop — so an action raised FROM the sheet
            // would put its own confirmation underneath it, and Delete would look like a button that does
            // nothing. Retry never hit this: it passes confirm: null and goes straight to ExecuteAsync,
            // which clears _expanded itself.
            _expanded = null;
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

    // The question, in the flow, where it cannot be missed.
    private Component? ConfirmPrompt() =>
        _pending is { } pending
            ? UiNotice.Tone("warn")[
                Span.Class("min-w-0 grow break-words")[pending.Prompt],
                UiButton.Key("confirm").Label("Confirm").Tone(UiTone.Danger)
                    .OnClickAsync(() => ExecuteAsync(pending.Action)),
                UiButton.Key("cancel").Label("Cancel").OnClick(Cancel)
            ]
            : null;

    // The answer, out of the flow. An inline result pushed the whole table down the moment an action
    // completed, which on a polling page moves rows under the operator's pointer; a toast reports the same
    // thing and moves nothing.
    private Component? ResultToast() =>
        _message is { } message
            ? UiToast
                .Message(message)
                .Tone(message.StartsWith("Failed:", StringComparison.Ordinal) ? UiTone.Danger : null)
                .Dismiss(Dismiss)
            : null;

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

        // justify-between rather than a centred group: on a phone this puts the two controls at the edges,
        // which is where thumbs are.
        return Div.Class("mt-4 flex items-center justify-between gap-3")[
            UiButton.Key("prev").Label("Previous")
                .Disabled(_page == 0)
                .OnClickAsync(() => GoAsync(_page - 1)),
            Span.Class("text-center text-xs text-ui-muted")[
                Span[$"Page {_page + 1} of {pages}"],
                // The total is the first thing to go when there is no room for it.
                Span.Class("hidden sm:inline")[$" — {_total} rows"]
            ],
            UiButton.Key("next").Label("Next")
                .Disabled(_page >= pages - 1)
                .OnClickAsync(() => GoAsync(_page + 1))
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
