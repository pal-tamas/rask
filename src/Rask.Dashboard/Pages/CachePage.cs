using Rask.Core.Routing;
using Rask.Dashboard.Panels;

namespace Rask.Dashboard.Pages;

/// <summary>
/// What's in the cache. Neither <c>ICache</c> nor <c>IDistributedCache</c> can enumerate keys, so this
/// reads the table directly — which is the point of a DB-backed cache.
/// </summary>
[Route("cache")]
[ParentRoute(typeof(DashboardLayout))]
public sealed class CachePage(
    ICachePanelReader cache,
    RaskDashboardOptions options,
    TimeProvider timeProvider) : PollingPanel
{
    private CacheStats _stats;
    private IReadOnlyList<CacheKeyRow> _rows = [];
    private int _total;
    private int _page;

    /// <summary>Substring filter on the key, from the query string so a search is a shareable link.</summary>
    [QueryParam("q")]
    public string? Search { get; set; }

    protected override RaskDashboardOptions Options => options;

    protected override async Task<object?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!cache.IsAvailable)
        {
            return null;
        }

        _stats = await cache.StatsAsync(cancellationToken).ConfigureAwait(false);
        (_rows, _total) = await cache
            .PageAsync(Search, _page * options.PageSize, options.PageSize, cancellationToken)
            .ConfigureAwait(false);

        return string.Join('|',
            [$"{_stats.Entries}:{_stats.Bytes}:{_stats.Expired}:{_total}",
             .. _rows.Select(r => $"{r.Key}:{r.ExpiresAt.Ticks}")]);
    }

    protected override Component? Render()
    {
        if (IsLoading)
        {
            return DashboardParts.Loading();
        }

        if (!cache.IsAvailable)
        {
            return DashboardParts.Empty(
                "Cache isn't registered",
                "Call AddRaskCache<TContext>() and modelBuilder.AddRaskCache() to see cache entries here.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return [
            H1(Class: "h4 mb-3")["Cache"],
            DashboardParts.Error(LoadError),
            BsRow(Class: "g-3 mb-4")[
                BsCol(Sm: 4)[BsStat(Value: _stats.Entries.ToString(), Label: "Entries", Icon: BsIconName.Archive)],
                BsCol(Sm: 4)[BsStat(Value: DashboardParts.Bytes(_stats.Bytes), Label: "Stored", Icon: BsIconName.Database)],
                BsCol(Sm: 4)[BsStat(
                    Value: _stats.Expired.ToString(),
                    Label: "Expired, not yet swept",
                    Icon: BsIconName.ClockHistory,
                    Caption: "removed by the purge sweep")]
            ],
            _rows.Count == 0
                ? DashboardParts.Empty(
                    Search is { Length: > 0 } ? $"No keys matching \"{Search}\"" : "Cache is empty",
                    "Entries appear here as soon as something is cached.")
                : Table(now),
            Pager(),
            DashboardParts.Parked(IsParked, ResumeAsync),
        ];
    }

    private Component Table(DateTime now) =>
        BsTable(Small: true, Hover: true, Responsive: true)[
            Thead()[Tr()[Th()["Key"], Th()["Size"], Th()["Written"], Th()["Expires"], Th()["Sliding"]]],
            Tbody()[_rows.Select(r => Tr(Key: r.Key, Class: r.ExpiresAt <= now ? "text-body-secondary" : null)[
                Td()[Div(Class: "text-truncate font-monospace small", Style: "max-width:28rem")[r.Key]],
                Td()[DashboardParts.Bytes(r.Bytes)],
                Td()[DashboardParts.Ago(r.CreatedAt, now)],
                Td()[
                    r.ExpiresAt <= now
                        ? BsBadge(Color: BsColor.Secondary)["expired"]
                        : Span()[DashboardParts.Ago(r.ExpiresAt, now)]
                ],
                Td()[r.SlidingSeconds is { } s ? DashboardParts.Duration(TimeSpan.FromSeconds(s)) : "—"]
            ])]
        ];

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
            Span(Class: "small text-body-secondary")[$"Page {_page + 1} of {pages} — {_total} keys"],
            BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm,
                Disabled: _page >= pages - 1, OnClickAsync: () => GoAsync(_page + 1))["Next"]
        ];
    }

    private async Task GoAsync(int page)
    {
        _page = Math.Max(0, page);
        await LoadAsync(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }
}
