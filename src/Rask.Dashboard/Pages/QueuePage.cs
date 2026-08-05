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
public sealed class QueuePage(
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

    /// <summary>Which queue, from the route.</summary>
    [RouteParam]
    public string Queue { get; set; } = "";

    /// <summary>Which slice, from the query string, so a filtered view is a shareable link.</summary>
    [QueryParam("show")]
    public string? Show { get; set; }

    protected override RaskDashboardOptions Options => options;

    private QueueFilter Filter =>
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
            return DashboardParts.Loading();
        }

        if (_panel is null)
        {
            return DashboardParts.Empty(
                $"No queue called \"{Queue}\"",
                "Either that battery isn't registered, or its table isn't mapped into the DbContext.");
        }

        return [
            Div(Class: "d-flex align-items-center gap-2 mb-3")[
                BsIcon(Name: _panel.Icon, Class: "fs-4"),
                H1(Class: "h4 mb-0")[_panel.Title]
            ],
            DashboardParts.Error(LoadError),
            FilterTabs(),
            _rows.Count == 0 ? EmptyForFilter() : RowsTable(),
            Pager(),
            DashboardParts.Parked(IsParked, ResumeAsync),
        ];
    }

    private Component FilterTabs() =>
        BsNav(Class: "nav-pills gap-1 mb-3")[
            Tab(QueueFilter.Outstanding, "Outstanding", _counts.Outstanding, null),
            Tab(QueueFilter.Due, "Due", _counts.Due, null),
            Tab(QueueFilter.Delayed, "Delayed", _counts.Delayed, null),
            Tab(QueueFilter.Failed, "Failed", _counts.Failed, _counts.Failed > 0 ? BsColor.Danger : null),
            Tab(QueueFilter.Processed, "Processed", _counts.Processed, null)
        ];

    private Component Tab(QueueFilter filter, string label, int count, BsColor? tone) =>
        BsNavItem()[
            BsLink(
                Routes.QueuePage(_panel!.Slug, Show: filter.ToString().ToLowerInvariant()),
                Class: Bs.Join("nav-link d-flex align-items-center gap-2", Filter == filter ? "active" : null))[
                Span()[label],
                BsBadge(Color: tone ?? (Filter == filter ? BsColor.Light : BsColor.Secondary), Pill: true)[count.ToString()]
            ]
        ];

    private Component EmptyForFilter() => DashboardParts.Empty(
        $"Nothing {Filter.ToString().ToLowerInvariant()}",
        Filter == QueueFilter.Failed
            ? "No dead letters. This is the number you want at zero."
            : "Nothing in this slice right now.");

    private Component RowsTable()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return BsTable(Small: true, Hover: true, Responsive: true)[
            Thead()[Tr()[
                Th()["#"], Th()[TypeColumnLabel()], Th()["When"], Th()["Attempts"], Th()["Status"], Th()
            ]],
            Tbody()[_rows.SelectMany(r => Row(r, now))]
        ];
    }

    // Mail's "type" column is really its subject; calling it Type on that page would be a small lie.
    private string TypeColumnLabel() => _panel!.Slug == "mail" ? "Subject" : "Type";

    private IEnumerable<Component> Row(QueueRow row, DateTime now)
    {
        var isDead = row.ProcessedAt is null && row.Attempts >= _panel!.MaxAttempts;
        yield return Tr(Key: row.Id, Class: isDead ? "table-danger" : null)[
            Td(Class: "text-body-secondary")[row.Id.ToString()],
            Td()[Div(Class: "text-truncate", Style: "max-width:28rem")[row.Type]],
            Td()[DashboardParts.Ago(row.CreatedAt, now)],
            Td()[row.Attempts.ToString()],
            Td()[StatusBadge(row, isDead, now)],
            Td(Class: "text-end")[
                BsButton(
                    Color: BsColor.Secondary,
                    Outline: true,
                    Size: BsSize.Sm,
                    OnClick: () => Toggle(row.Id))[_expanded == row.Id ? "Hide" : "Details"]
            ]
        ];

        if (_expanded == row.Id)
        {
            yield return Tr(Key: $"{row.Id}-details")[Td(Colspan: 6, Class: "bg-body-tertiary")[Details(row)]];
        }
    }

    private Component StatusBadge(QueueRow row, bool isDead, DateTime now) => row switch
    {
        { ProcessedAt: not null } => BsBadge(Color: BsColor.Success)["done"],
        _ when isDead => BsBadge(Color: BsColor.Danger)["dead letter"],
        _ when row.RunAt > now => BsBadge(Color: BsColor.Secondary)[$"retries in {DashboardParts.Duration(row.RunAt - now)}"],
        _ => BsBadge(Color: BsColor.Info)["due"],
    };

    private static Component Details(QueueRow row) =>
        Div(Class: "small")[
            row.Error is { } error
                ? Div(Class: "mb-2")[
                    Div(Class: "fw-semibold text-danger")["Last error"],
                    Pre(Class: "mb-0 text-body-secondary text-wrap")[error]
                ]
                : null,
            Div(Class: "fw-semibold")["Payload"],
            Pre(Class: "mb-0 text-body-secondary text-wrap")[row.Payload]
        ];

    private void Toggle(long id)
    {
        _expanded = _expanded == id ? null : id;
        StateHasChanged();
    }

    private Component? Pager()
    {
        var pages = (int)Math.Ceiling(_total / (double)options.PageSize);
        if (pages <= 1)
        {
            return null;
        }

        return Div(Class: "d-flex align-items-center gap-2")[
            BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm,
                Disabled: _page == 0, OnClickAsync: () => GoAsync(_page - 1))["Previous"],
            Span(Class: "small text-body-secondary")[$"Page {_page + 1} of {pages} — {_total} rows"],
            BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm,
                Disabled: _page >= pages - 1, OnClickAsync: () => GoAsync(_page + 1))["Next"]
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
