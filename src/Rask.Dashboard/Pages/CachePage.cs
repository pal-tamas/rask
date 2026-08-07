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
    private string? _message;
    private bool _confirmFlush;

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
            Div(Class: "d-flex align-items-center gap-2 mb-3")[
                H1(Class: "h4 mb-0")["Cache"],
                Div(Class: "ms-auto")[FlushButton()]
            ],
            DashboardParts.Error(LoadError),
            ActionResult(),
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

    private new Component Table(DateTime now) =>
        BsTable(Small: true, Hover: true, Responsive: true)[
            Thead()[Tr()[Th()["Key"], Th()["Size"], Th()["Written"], Th()["Expires"], Th()["Sliding"], Th()]],
            Tbody()[_rows.Select(r => Tr(Key: r.Key, Class: r.ExpiresAt <= now ? "text-body-secondary" : null)[
                Td()[Div(Class: "text-truncate font-monospace small", Style: "max-width:28rem", Title: r.Key)[r.Key]],
                Td()[DashboardParts.Bytes(r.Bytes)],
                Td(Title: r.CreatedAt.ToString("u"))[DashboardParts.Ago(r.CreatedAt, now)],
                Td(Title: r.ExpiresAt.ToString("u"))[
                    r.ExpiresAt <= now
                        ? BsBadge(Color: BsColor.Secondary)["expired"]
                        : Span()[DashboardParts.Ago(r.ExpiresAt, now)]
                ],
                Td()[r.SlidingSeconds is { } s ? DashboardParts.Duration(TimeSpan.FromSeconds(s)) : "—"],
                Td(Class: "text-end")[EvictButton(r.Key)]
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

    // Evicting one key is a recompute, not a lost fact, so it sits in the Safe tier and needs no
    // confirmation. Flushing everything is correctness-safe too, but a cold cache on a busy app means a
    // stampede — hence the Destructive tier and a confirmation.
    private Component? EvictButton(string key) =>
        options.Actions.HasFlag(RaskDashboardActions.Safe)
            ? BsButton(
                Color: BsColor.Secondary,
                Outline: true,
                Size: BsSize.Sm,
                OnClickAsync: () => EvictAsync(key))["Evict"]
            : null;

    private Component? FlushButton() =>
        options.Actions.HasFlag(RaskDashboardActions.Destructive) && _stats.Entries > 0
            ? BsButton(Color: BsColor.Danger, Size: BsSize.Sm, OnClick: () => Confirm(true))["Flush cache"]
            : null;

    private Component? ActionResult()
    {
        if (_confirmFlush)
        {
            return BsAlert(Color: BsColor.Warning, Class: "d-flex align-items-center gap-2")[
                Span(Class: "flex-grow-1")[
                    $"Drop all {_stats.Entries} cache entries? Nothing is lost permanently, but everything is recomputed at once."
                ],
                BsButton(Color: BsColor.Danger, Size: BsSize.Sm, OnClickAsync: FlushAsync)["Confirm"],
                BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, OnClick: () => Confirm(false))["Cancel"]
            ];
        }

        return _message is { } message
            ? BsAlert(Color: BsColor.Info, Class: "d-flex align-items-center gap-2")[
                Span(Class: "flex-grow-1")[message],
                BsCloseButton(OnClick: Dismiss)
            ]
            : null;
    }

    private void Confirm(bool pending)
    {
        _confirmFlush = pending;
        StateHasChanged();
    }

    private void Dismiss()
    {
        _message = null;
        StateHasChanged();
    }

    private async Task EvictAsync(string key)
    {
        var removed = await cache.EvictAsync(key, CancellationToken).ConfigureAwait(false);
        _message = removed > 0 ? $"Evicted \"{key}\"." : $"\"{key}\" was already gone.";
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task FlushAsync()
    {
        _confirmFlush = false;
        _message = $"Flushed {await cache.FlushAsync(CancellationToken).ConfigureAwait(false)} entries.";
        _page = 0;
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task RefreshAsync()
    {
        await LoadAsync(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }

    private async Task GoAsync(int page)
    {
        _page = Math.Max(0, page);
        await LoadAsync(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }
}
