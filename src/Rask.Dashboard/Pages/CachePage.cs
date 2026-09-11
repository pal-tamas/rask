using Rask.Core.Routing;
using Rask.Dashboard.Panels;

namespace Rask.Dashboard.Pages;

/// <summary>
/// What's in the cache. Neither <c>ICache</c> nor <c>IDistributedCache</c> can enumerate keys, so this
/// reads the table directly — which is the point of a DB-backed cache.
/// </summary>
[Route("cache")]
[ParentRoute(typeof(DashboardLayout))]
public sealed partial class CachePage(
    ICachePanelReader cache,
    RaskDashboardOptions options,
    TimeProvider timeProvider,
    Navigator navigator) : PollingPanel
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

    /// <inheritdoc />
    protected override RaskDashboardOptions Options => options;

    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (IsLoading)
        {
            return DashboardLoading;
        }

        if (!cache.IsAvailable)
        {
            return UiCard[
                UiEmpty
                    .Heading("Cache isn't registered")
                    .Detail("Call AddRaskCache<TContext>() and modelBuilder.AddRaskCache() to see cache entries here.")
            ];
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return [
            UiHeader.Heading("Cache").Actions(FlushButton()),
            DashboardError.Message(LoadError),
            ConfirmPrompt(),
            UiMetricRow.Columns(3)[
                UiMetric.Key("entries").Label("Entries").Value(_stats.Entries.ToString()),
                UiMetric.Key("stored").Label("Stored").Value(DashboardParts.Bytes(_stats.Bytes)),
                UiMetric
                    .Key("expired")
                    .Label("Expired, not swept")
                    .Value(_stats.Expired.ToString())
                    .Caption("removed by the purge sweep")
            ],
            KeyGrid(now),
            DashboardParked.Parked(IsParked).Resume(ResumeAsync),
            ResultToast(),
        ];
    }

    private Task SearchAsync(string value)
    {
        // Navigate, do not merely reload (#936). Search is declared [QueryParam("q")] and its own summary
        // calls the result a shareable link, but assigning it and re-rendering left the address bar on
        // the previous URL — so the bar and the page disagreed, and copying the link lost the search.
        //
        // Only the search reaches the URL. Paging is deliberately left as page-local state: `_page` is a
        // field rather than a [QueryParam], so putting it in the link here would produce a URL that does
        // not restore, which is worse than one that plainly carries less.
        Search = string.IsNullOrWhiteSpace(value) ? null : value;
        _page = 0;
        navigator.NavigateTo(Routes.CachePage(Search: Search));
        return Task.CompletedTask;
    }

    // The search lives in the grid's toolbar so it survives an empty result — a search that matched nothing
    // must leave the box that typed it on the screen. The key is the column every width keeps.
    private Component KeyGrid(DateTime now) =>
        UiDataGrid.Data(_rows)
            .RowKey(r => r.Key)
            .Label("Cache keys")
            .PageSize(options.PageSize)
            .Page(_page)
            .TotalCount(_total)
            .OnPageChange(GoAsync)
            .Toolbar(UiSearch
                .Placeholder("Search keys")
                .AccessibleLabel("Search cache keys")
                .Value(Search)
                .OnSearch(SearchAsync))
            .Empty(UiEmpty
                .Heading(Search is { Length: > 0 } ? $"No keys matching \"{Search}\"" : "Cache is empty")
                .Detail("Entries appear here as soon as something is cached."))[c => [
                c.Field(r => r.Key).Title("Key").Mono(true),
                c.Field(r => r.Bytes).Title("Size").Value(r => DashboardParts.Bytes(r.Bytes)),
                c.Field(r => r.CreatedAt).Title("Written").ShowFrom(UiBreakpoint.Md).Cell(r =>
                    Span.Title(r.CreatedAt.ToString("u"))[DashboardParts.Ago(r.CreatedAt, now)]),
                c.Field(r => r.ExpiresAt).Title("Expires").Cell(r =>
                    r.ExpiresAt <= now
                        ? UiBadge["expired"]
                        : Span.Title(r.ExpiresAt.ToString("u"))[DashboardParts.Ago(r.ExpiresAt, now)]),
                c.Field(r => r.SlidingSeconds).Title("Sliding").ShowFrom(UiBreakpoint.Lg).Value(r =>
                    r.SlidingSeconds is { } s ? DashboardParts.Duration(TimeSpan.FromSeconds(s)) : "—"),
                // Evicting one key is a recompute, not a lost fact, so it sits in the Safe tier and needs no
                // confirmation. Flushing everything is correctness-safe too, but a cold cache on a busy app
                // means a stampede — hence the Destructive tier and a confirmation.
                options.Actions.HasFlag(RaskDashboardActions.Safe)
                    ? c.Column().Cell(r => UiButton.Size(UiSize.Sm).OnClick(() => EvictAsync(r.Key))["Evict"])
                    : null,
            ]];

    private Component? FlushButton() =>
        options.Actions.HasFlag(RaskDashboardActions.Destructive) && _stats.Entries > 0
            ? UiButton.Tone(UiTone.Error).OnClick(() => Confirm(true))[UiIcon.Name(UiIconName.Trash), "Flush cache"]
            : null;

    private Component? ConfirmPrompt() =>
        _confirmFlush
            ? UiAlert.Tone(UiTone.Warning)[
                Span[
                    $"Drop all {_stats.Entries} cache entries? Nothing is lost permanently, but everything is recomputed at once."
                ],
                Div[
                    UiButton.Key("confirm").Tone(UiTone.Error).Size(UiSize.Sm).OnClick(FlushAsync)["Confirm"],
                    " ",
                    UiButton.Key("cancel").Size(UiSize.Sm).OnClick(() => Confirm(false))["Cancel"]
                ]
            ]
            : null;

    private Component? ResultToast() =>
        _message is { } message ? UiToast.Message(message).Dismiss(Dismiss) : null;

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
