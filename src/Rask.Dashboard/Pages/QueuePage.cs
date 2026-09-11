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

        if (_rows.Count == 0 && _page > DashboardParts.LastPageIndex(_total, options.PageSize))
        {
            _page = DashboardParts.LastPageIndex(_total, options.PageSize);
            (_rows, _total) = await _panel
                .PageAsync(Filter, _page * options.PageSize, options.PageSize, cancellationToken)
                .ConfigureAwait(false);
        }

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
            return UiCard[
                UiEmpty
                    .Heading($"No queue called \"{Queue}\"")
                    .Detail("Either that battery isn't registered, or its table isn't mapped into the DbContext.")
            ];
        }

        return [
            UiHeader
                .Heading(_panel.Title)
                .Icon(_panel.Icon)
                .Actions([.. QueueActionButtons()]),
            DashboardError.Message(LoadError),
            // A question stays in the flow: it has to be answered before anything else means anything, and
            // a toast is the wrong shape for something you must respond to.
            ConfirmPrompt(),
            CountTiles(),
            RowsGrid(),
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
        UiMetricRow.Columns(5)[
            Tile(QueueFilter.Outstanding, "Outstanding", _counts.Outstanding, tone: null),
            Tile(QueueFilter.Due, "Due", _counts.Due, tone: null),
            Tile(QueueFilter.Delayed, "Delayed", _counts.Delayed, tone: null),
            Tile(QueueFilter.Failed, "Failed", _counts.Failed, tone: _counts.Failed > 0 ? UiTone.Error : null),
            Tile(QueueFilter.Processed, "Processed", _counts.Processed, tone: null)
        ];

    private Component Tile(QueueFilter filter, string label, int count, UiTone? tone) =>
        UiMetric
            .Key(label)
            .Label(label)
            .Value(count.ToString())
            .Tone(tone)
            .Href(Routes.QueuePage(_panel!.Slug, Show: filter.ToString().ToLowerInvariant()))
            .Active(Filter == filter);

    private bool IsDead(QueueRow row) => row.ProcessedAt is null && row.Attempts >= _panel!.MaxAttempts;

    /// <summary>
    /// The rows behind the selected tile.
    /// </summary>
    /// <remarks>
    /// The type is the column that survives every width. The attempt count waits until the table has room
    /// for it, and below <c>sm</c> every column is its own labelled line rather than a column an operator has
    /// to swipe sideways to find. A dead letter carries the error tone as well as its badge: it is the row
    /// this page exists for.
    /// </remarks>
    private Component RowsGrid()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var slice = Filter.ToString().ToLowerInvariant();

        return UiDataGrid.Data(_rows)
            .RowKey(r => r.Id)
            .Label($"{_panel!.Title}, {slice}")
            .PageSize(options.PageSize)
            .Page(_page)
            .TotalCount(_total)
            .OnPageChange(GoAsync)
            .RowTone(r => IsDead(r) ? UiTone.Error : null)
            .Empty(UiEmpty
                .Heading($"Nothing {slice}")
                .Detail(Filter == QueueFilter.Failed
                    ? "No dead letters. This is the number you want at zero."
                    : "Nothing in this slice right now."))[c => [
                c.Field(r => r.Id).Title("#").Mono(true),
                c.Field(r => r.Type).Title(TypeColumnLabel()),
                c.Field(r => r.CreatedAt).Title("When").Cell(r =>
                    Span.Title(r.CreatedAt.ToString("u"))[DashboardParts.Ago(r.CreatedAt, now)]),
                c.Field(r => r.Attempts).Title("Attempts").ShowFrom(UiBreakpoint.Md),
                c.Column().Title("Status").Cell(r => StatusBadge(r, IsDead(r), now)),
                c.Column().Cell(r => [.. RowButtons(r, IsDead(r))]),
            ]];
    }

    // Mail's "type" column is really its subject; calling it Type on that page would be a small lie.
    private string TypeColumnLabel() => _panel!.Slug == "mail" ? "Subject" : "Type";

    // Keyed buttons and nothing between them. The grid cell spaces adjacent buttons itself; a " " text node here
    // mixed unkeyed text into keyed siblings, which makes the live diff match by position — so after a Retry the
    // focused Retry button could be patched into Delete in place, one Enter away from the delete prompt.
    private IEnumerable<Component> RowButtons(QueueRow row, bool isDead)
    {
        foreach (var button in RowActionButtons(row, isDead))
        {
            yield return button;
        }

        // Opens the detail sheet. A button rather than a clickable row: a <tr> is not focusable, and the
        // console has no script to make one behave like a control.
        yield return UiButton.Key("details").Size(UiSize.Sm)
            .OnClick(() => Open(row.Id))[UiIcon.Name(UiIconName.ChevronRight), "Details"];
    }

    private static string StatusText(QueueRow row, bool isDead, DateTime now) => row switch
    {
        { ProcessedAt: not null } => "done",
        _ when isDead => "dead letter",
        _ when row.RunAt > now => $"retries in {DashboardParts.Duration(row.RunAt - now)}",
        _ => "due",
    };

    private static Component StatusBadge(QueueRow row, bool isDead, DateTime now) =>
        UiBadge.Tone(row switch
        {
            { ProcessedAt: not null } => UiTone.Success,
            _ when isDead => UiTone.Error,
            _ when row.RunAt > now => (UiTone?)null,
            _ => UiTone.Info,
        })[StatusText(row, isDead, now)];

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
        var isDead = IsDead(row);

        // Rendered only while a row is selected, and Close flips that back — which is the whole of the
        // dialog's open state. UiModal.Open is for a sheet kept mounted while hidden; this one has
        // nothing to preserve between openings, so not rendering it at all is cheaper and simpler.
        return UiModal
            .Title(row.Type)
            .Close(Close)
            .Footer([.. RowActionButtons(row, isDead), UiButton.Key("close").OnClick(Close)["Close"]])[
            UiDetailList[
                UiDetailRow.Key("id").Label("ID").Value($"#{row.Id}").Mono(true),
                UiDetailRow.Key("queue").Label("Queue").Value(_panel!.Title),
                UiDetailRow.Key("status").Label("Status").Value(StatusText(row, isDead, now))
                    .Tone(isDead ? UiTone.Error : null),
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
            row.Error is { } error ? UiCode.Content(error).Label("Last error").Tone(UiTone.Error) : null,
            UiCode.Content(row.Payload).Label("Payload")
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
            yield return UiButton
                .Key("retry-all")
                .Tone(UiTone.Error)
                .Variant(UiVariant.Outline)
                .OnClick(() => RunAsync(
                    $"Retry all {_counts.Failed} dead letters?",
                    async ct => $"Re-queued {await _panel!.RetryAllAsync(ct).ConfigureAwait(false)}."))[
                UiIcon.Name(UiIconName.Retry),
                "Retry all failed"
            ];
        }

        if (_counts.Processed > 0)
        {
            yield return UiButton
                .Key("purge")
                .OnClick(() => RunAsync(
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
            yield return UiButton
                .Key("retry")
                .Tone(UiTone.Error)
                .Variant(UiVariant.Outline)
                .Size(UiSize.Sm)
                .OnClick(() => RunAsync(
                    null,   // retrying one dead letter is reversible enough not to need a confirmation
                    async ct => await _panel!.RetryAsync(row.Id, ct).ConfigureAwait(false) > 0
                        ? $"Re-queued #{row.Id}."
                        : $"#{row.Id} was already picked up."))["Retry"];
        }

        if (row.ProcessedAt is null && options.Actions.HasFlag(RaskDashboardActions.Destructive))
        {
            yield return UiButton
                .Key("delete")
                .Tone(UiTone.Error)
                .Variant(UiVariant.Outline)
                .Size(UiSize.Sm)
                .OnClick(() => RunAsync(
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
            // while the sheet is a modal over a backdrop — so an action raised FROM the sheet would put its
            // own confirmation underneath it, and Delete would look like a button that does nothing. Retry
            // never hit this: it passes confirm: null and goes straight to ExecuteAsync, which clears
            // _expanded itself.
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

    // The question, in the flow, where it cannot be missed. The two answers share one cell of the alert, so
    // on a phone the question keeps the width and the buttons sit together beneath it.
    private Component? ConfirmPrompt() =>
        _pending is { } pending
            ? UiAlert.Tone(UiTone.Warning)[
                Span[pending.Prompt],
                Div[
                    UiButton.Key("confirm").Tone(UiTone.Error).Size(UiSize.Sm)
                        .OnClick(() => ExecuteAsync(pending.Action))["Confirm"],
                    " ",
                    UiButton.Key("cancel").Size(UiSize.Sm).OnClick(Cancel)["Cancel"]
                ]
            ]
            : null;

    // The answer, out of the flow. An inline result pushed the whole table down the moment an action
    // completed, which on a polling page moves rows under the operator's pointer; a toast reports the same
    // thing and moves nothing.
    private Component? ResultToast() =>
        _message is { } message
            ? UiToast
                .Message(message)
                .Tone(message.StartsWith("Failed:", StringComparison.Ordinal) ? UiTone.Error : null)
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

    private async Task GoAsync(int page)
    {
        _page = Math.Max(0, page);
        _expanded = null;
        await LoadAsync(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }
}
