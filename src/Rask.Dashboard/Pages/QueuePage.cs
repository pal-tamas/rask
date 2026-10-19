using System.Globalization;
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
    OpsOptions options,
    TimeProvider timeProvider) : PollingPanel
{
    private IQueuePanel? _panel;
    private QueueCounts _counts;
    private IReadOnlyList<QueueRow> _rows = [];
    private int _total;
    private int _page;
    private long? _expanded;
    private (string Prompt, Func<CancellationToken, Task<string>> Action)? _pending;

    /// <summary>Which queue, from the route.</summary>
    [RouteParam]
    public string Queue { get; set; } = "";

    /// <summary>Which slice, from the query string, so a filtered view is a shareable link.</summary>
    [QueryParam("show")]
    public string? Show { get; set; }

    /// <inheritdoc />
    protected override OpsOptions Options => options;

    private QueueFilter Filter =>
        Enum.TryParse<QueueFilter>(Show, ignoreCase: true, out var parsed) ? parsed : QueueFilter.Outstanding;

    /// <inheritdoc />
    protected override async Task<object?> Load(CancellationToken cancellationToken)
    {
        _panel = queues.FirstOrDefault(q =>
            string.Equals(q.Slug, Queue, StringComparison.OrdinalIgnoreCase) && q.IsAvailable);

        if (_panel is null)
        {
            _rows = [];
            _total = 0;
            return null;
        }

        _counts = await _panel.Counts(cancellationToken).ConfigureAwait(false);
        (_rows, _total) = await _panel
            .Page(Filter, _page * options.PageSize, options.PageSize, cancellationToken)
            .ConfigureAwait(false);

        if (_rows.Count == 0 && _page > DashboardParts.LastPageIndex(_total, options.PageSize))
        {
            _page = DashboardParts.LastPageIndex(_total, options.PageSize);
            (_rows, _total) = await _panel
                .Page(Filter, _page * options.PageSize, options.PageSize, cancellationToken)
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
            return Ui.Card[
                DashboardEmpty
                    .Title($"No queue called \"{Queue}\"")
                    .Detail("Either that battery isn't registered, or its table isn't mapped into the DbContext.")
            ];
        }

        return [
            DashboardHeading
                .Title(_panel.Title)
                .Actions([.. QueueActionButtons()]),
            DashboardError.Message(LoadError),
            // A question stays in the flow: it has to be answered before anything else means anything, and
            // a toast is the wrong shape for something you must respond to.
            ConfirmPrompt(),
            CountFilters(),
            RowsTable(),
            DashboardParked.Parked(IsParked).Resume(Resume),
            DetailSheet(),
        ];
    }

    /// <summary>
    /// The counts, as the control that selects which of them you are looking at.
    /// </summary>
    /// <remarks>
    /// Flux's navbar, each count a badge on its item. One row that both reports and filters is fewer things on the
    /// screen and one fewer place for the two to disagree — and because each item is a real link carrying
    /// <c>?show=</c>, the selection is shareable and keyboard-reachable. It scrolls sideways on a phone rather than
    /// wrapping a count away from its name.
    /// </remarks>
    private Component CountFilters() =>
        Div.Class("-mx-2 overflow-x-auto px-2")[
            Ui.Navbar.Class("py-0")[
                CountItem(QueueFilter.Outstanding, "Outstanding", _counts.Outstanding, alarming: false),
                CountItem(QueueFilter.Due, "Due", _counts.Due, alarming: false),
                CountItem(QueueFilter.Delayed, "Delayed", _counts.Delayed, alarming: false),
                CountItem(QueueFilter.Failed, "Failed", _counts.Failed, alarming: _counts.Failed > 0),
                CountItem(QueueFilter.Processed, "Processed", _counts.Processed, alarming: false)
            ]
        ];

    private Component CountItem(QueueFilter filter, string label, int count, bool alarming) =>
        Ui.NavbarItem
            .Key(label)
            .Href(Routes.QueuePage(_panel!.Slug, Show: filter.ToString().ToLowerInvariant()))
            .Current(Filter == filter)
            .Badge(count.ToString(CultureInfo.CurrentCulture))
            .BadgeColor(alarming ? Ui.Color.Red : null)[label];

    private bool IsDead(QueueRow row) => row.ProcessedAt is null && row.Attempts >= _panel!.MaxAttempts;

    /// <summary>
    /// The rows behind the selected count.
    /// </summary>
    /// <remarks>
    /// The type is the one column without a width, so a page or a filter that brings other rows moves no column; a
    /// long type ends in an ellipsis with the whole of it in its tooltip. A dead letter's badge is red: it is the
    /// row this page exists for. On a phone the table scrolls inside its own box.
    /// </remarks>
    private Component RowsTable()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var slice = Filter.ToString().ToLowerInvariant();

        if (_rows.Count == 0)
        {
            return Ui.Card[
                DashboardEmpty
                    .Title($"Nothing {slice}")
                    .Detail(Filter == QueueFilter.Failed
                        ? "No dead letters. This is the number you want at zero."
                        : "Nothing in this slice right now.")
            ];
        }

        return Ui.Table
            .Class("w-full min-w-176 md:min-w-240")
            .AriaLabel($"{_panel!.Title}, {slice}")
            .Paginate(Ui.Pagination
                .Paginator(new UiPaginator { Page = _page + 1, PerPage = options.PageSize, Total = _total })
                .OnPage(page => GoAsync(page - 1)))[
            Ui.TableColumns[
                Ui.TableColumn.Class("w-24 hidden md:table-cell")["#"],
                // Flush with the page below md, where the id before it has no column: Flux unpads only a first cell.
                Ui.TableColumn.Class("max-md:ps-0")[TypeColumnLabel()],
                Ui.TableColumn.Class("w-28")["When"],
                Ui.TableColumn.Class("w-24 hidden md:table-cell")["Attempts"],
                Ui.TableColumn.Class("w-36")["Status"],
                Ui.TableColumn.Class("w-60")
            ],
            Ui.TableRows[
                _rows.Select(r => Ui.TableRow.Key(r.Id)[
                    Ui.TableCell.Class("hidden font-mono md:table-cell")[$"{r.Id}"],
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate max-md:ps-0").Title(r.Type)[r.Type],
                    Ui.TableCell[Span.Title(r.CreatedAt.ToString("u"))[DashboardParts.Ago(r.CreatedAt, now)]],
                    Ui.TableCell.Class("hidden md:table-cell")[$"{r.Attempts}"],
                    Ui.TableCell[StatusBadge(r, IsDead(r), now)],
                    Ui.TableCell.Class("py-0")[Div.Class("flex justify-end gap-2")[RowButtons(r, IsDead(r))]]
                ])
            ]
        ];
    }

    // Mail's "type" column is really its subject; calling it Type on that page would be a small lie.
    private string TypeColumnLabel() => string.Equals(_panel!.Slug, "mail", StringComparison.Ordinal) ? "Subject" : "Type";

    // Keyed buttons and nothing between them. The cell's flex row spaces them; a " " text node here
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
        yield return Ui.Button.Sm.Icon(Ui.IconName.ChevronRight).Key("details")
            .OnClick(() => Open(row.Id))["Details"];
    }

    private static string StatusText(QueueRow row, bool isDead, DateTime now) => row switch
    {
        { ProcessedAt: not null } => "done",
        _ when isDead => "dead letter",
        _ when row.RunAt > now => $"retries in {DashboardParts.Duration(row.RunAt - now)}",
        _ => "due",
    };

    private static Component StatusBadge(QueueRow row, bool isDead, DateTime now) =>
        Ui.Badge.Sm.Color(row switch
        {
            { ProcessedAt: not null } => Ui.Color.Green,
            _ when isDead => Ui.Color.Red,
            _ when row.RunAt > now => (Ui.Color?)null,
            _ => Ui.Color.Blue,
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

        Component actions = [.. RowActionButtons(row, isDead), Ui.Button.Sm.Key("close").OnClick(Close)["Close"]];

        // Rendered only while a row is selected, and Close flips that back — which is the whole of the
        // dialog's open state. Ui.Modal.Open is for a sheet kept mounted while hidden; this one has
        // nothing to preserve between openings, so not rendering it at all is cheaper and simpler.
        return Ui.Modal.OnClose(Close)[
            Div.Class("space-y-6")[
                Ui.Heading.Level(2).Lg[row.Type],
                DashboardFacts[
                    DashboardFact.Key("id").Label("ID").Value($"#{row.Id}").Mono(true),
                    DashboardFact.Key("queue").Label("Queue").Value(_panel!.Title),
                    DashboardFact.Key("status").Label("Status").Value(StatusText(row, isDead, now))
                        .Danger(isDead),
                    DashboardFact.Key("attempts").Label("Total attempts")
                        .Value($"{row.Attempts} of {_panel.MaxAttempts}").Mono(true),
                    DashboardFact.Key("created").Label("Queued time").Value(row.CreatedAt.ToString("u")).Mono(true),
                    DashboardFact.Key("runat").Label(row.ProcessedAt is null ? "Runs at" : "Started")
                        .Value(row.RunAt.ToString("u")).Mono(true),
                    row.ProcessedAt is { } done
                        ? DashboardFact.Key("done").Label("Processed").Value(done.ToString("u")).Mono(true)
                        : null,
                    DashboardFact.Key("age").Label("Age").Value(DashboardParts.Ago(row.CreatedAt, now))
                ],
                row.Error is { } error ? DashboardCode.Key("error").Content(error).Label("Last error").Danger(true) : null,
                DashboardCode.Key("payload").Content(row.Payload).Label("Payload"),
                Div.Class("flex flex-wrap gap-2 sm:justify-end")[actions]
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
            yield return Ui.Button.Red.Icon(Ui.IconName.ArrowPath)
                .Key("retry-all")
                .OnClick(() => RunAsync(
                    $"Retry all {_counts.Failed} dead letters?",
                    async ct => $"Re-queued {await _panel!.RetryAll(ct).ConfigureAwait(false)}."))[
                "Retry all failed"
            ];
        }

        if (_counts.Processed > 0)
        {
            yield return Ui.Button
                .Key("purge")
                .OnClick(() => RunAsync(
                    "Delete processed rows older than 7 days? Outstanding work and dead letters are kept.",
                    async ct => $"Purged {await _panel!.PurgeProcessed(TimeSpan.FromDays(7), ct).ConfigureAwait(false)}."))[
                "Purge processed"
            ];
        }
    }

    private IEnumerable<Component> RowActionButtons(QueueRow row, bool isDead)
    {
        if (isDead && options.Actions.HasFlag(RaskDashboardActions.Safe))
        {
            yield return Ui.Button.Red.Sm
                .Key("retry")
                .OnClick(() => RunAsync(
                    null,   // retrying one dead letter is reversible enough not to need a confirmation
                    async ct => await _panel!.Retry(row.Id, ct).ConfigureAwait(false) > 0
                        ? $"Re-queued #{row.Id}."
                        : $"#{row.Id} was already picked up."))["Retry"];
        }

        if (row.ProcessedAt is null && options.Actions.HasFlag(RaskDashboardActions.Destructive))
        {
            yield return Ui.Button.Red.Sm
                .Key("delete")
                .OnClick(() => RunAsync(
                    $"Delete #{row.Id}? The work is discarded and cannot be recovered.",
                    async ct => await _panel!.Delete(row.Id, ct).ConfigureAwait(false) > 0
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
        // The answer goes out of the flow, as a toast: an inline result pushed the whole table down the moment
        // an action completed, which on a polling page moves rows under the operator's pointer.
        try
        {
            Toast.Success(await action(CancellationToken).ConfigureAwait(false));
        }
#pragma warning disable CA1031 // A failed action must report itself, not tear the page down.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Toast.Error($"Failed: {ex.Message}");
        }

        _page = 0;
        _expanded = null;
        await Load(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }

    // The question, in the flow, where it cannot be missed. The two answers share one cell of the alert, so
    // on a phone the question keeps the width and the buttons sit together beneath it.
    private UiCallout? ConfirmPrompt() =>
        _pending is { } pending
            ? Ui.Callout.Warning.Inline().Role("alert").Heading(pending.Prompt).Actions([
                Ui.Button.Danger.Sm.Key("confirm")
                    .OnClick(() => ExecuteAsync(pending.Action))["Confirm"],
                Ui.Button.Sm.Key("cancel").OnClick(Cancel)["Cancel"]
            ])
            : null;

    private void Cancel()
    {
        _pending = null;
        StateHasChanged();
    }

    private async Task GoAsync(int page)
    {
        _page = Math.Max(0, page);
        _expanded = null;
        await Load(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }
}
