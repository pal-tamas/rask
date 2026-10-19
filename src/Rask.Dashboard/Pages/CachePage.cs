using System.Globalization;
using Rask.Core;
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
    OpsOptions options,
    TimeProvider timeProvider) : PollingPanel
{
    private CacheStats _stats;
    private IReadOnlyList<CacheKeyRow> _rows = [];
    private int _total;
    private int _page;
    private bool _confirmFlush;

    /// <summary>Substring filter on the key, from the query string so a search is a shareable link.</summary>
    [QueryParam("q")]
    public string? Search { get; set; }

    /// <inheritdoc />
    protected override OpsOptions Options => options;

    /// <inheritdoc />
    protected override async Task<object?> Load(CancellationToken cancellationToken)
    {
        if (!cache.IsAvailable)
        {
            return null;
        }

        _stats = await cache.Stats(cancellationToken).ConfigureAwait(false);
        (_rows, _total) = await cache
            .Page(Search, _page * options.PageSize, options.PageSize, cancellationToken)
            .ConfigureAwait(false);

        if (_rows.Count == 0 && _page > DashboardParts.LastPageIndex(_total, options.PageSize))
        {
            _page = DashboardParts.LastPageIndex(_total, options.PageSize);
            (_rows, _total) = await cache
                .Page(Search, _page * options.PageSize, options.PageSize, cancellationToken)
                .ConfigureAwait(false);
        }

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
            return Ui.Card[
                DashboardEmpty
                    .Title("Cache isn't registered")
                    .Detail("Call AddRaskCache<TContext>() and modelBuilder.AddRaskCache() to see cache entries here.")
            ];
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return [
            DashboardHeading.Title("Cache").Actions(FlushButton()),
            DashboardError.Message(LoadError),
            ConfirmPrompt(),
            Div.Class("grid gap-4 sm:grid-cols-3")[
                DashboardMetric.Key("entries").Label("Entries").Value(_stats.Entries.ToString(CultureInfo.CurrentCulture)),
                DashboardMetric.Key("stored").Label("Stored").Value(DashboardParts.Bytes(_stats.Bytes)),
                DashboardMetric
                    .Key("expired")
                    .Label("Expired, not swept")
                    .Value(_stats.Expired.ToString(CultureInfo.CurrentCulture))
                    .Caption("removed by the purge sweep")
            ],
            DashboardSearch.Placeholder("Search keys").Value(Search).OnChange(SearchAsync),
            KeyTable(now),
            DashboardParked.Parked(IsParked).Resume(Resume),
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
        Go.To(Routes.CachePage(Search: Search));
        return Task.CompletedTask;
    }

    // The search sits above the table, so it survives an empty result — a search that matched nothing must leave
    // the box that typed it on the screen. The key is the one column without a width: it takes what is left.
    private Component KeyTable(DateTime now)
    {
        if (_rows.Count == 0)
        {
            return Ui.Card[
                DashboardEmpty
                    .Title(Search is { Length: > 0 } ? $"No keys matching \"{Search}\"" : "Cache is empty")
                    .Detail("Entries appear here as soon as something is cached.")
            ];
        }

        // Evicting one key is a recompute, not a lost fact, so it sits in the Safe tier and needs no confirmation.
        // Flushing everything is correctness-safe too, but a cold cache on a busy app means a stampede — hence the
        // Destructive tier and a confirmation.
        var evicts = options.Actions.HasFlag(RaskDashboardActions.Safe);

        return Ui.Table
            .Class("w-full min-w-160")
            .AriaLabel("Cache keys")
            .Paginate(Ui.Pagination
                .Paginator(new UiPaginator { Page = _page + 1, PerPage = options.PageSize, Total = _total })
                .OnPage(page => GoAsync(page - 1)))[
            Ui.TableColumns[
                Ui.TableColumn["Key"],
                Ui.TableColumn.Class("w-24")["Size"],
                Ui.TableColumn.Class("w-32")["Written"],
                Ui.TableColumn.Class("w-32")["Expires"],
                Ui.TableColumn.Class("w-24")["Sliding"],
                evicts ? Ui.TableColumn.Class("w-24") : null
            ],
            Ui.TableRows[
                _rows.Select(r => Ui.TableRow.Key(r.Key)[
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate font-mono").Title(r.Key)[r.Key],
                    Ui.TableCell[DashboardParts.Bytes(r.Bytes)],
                    Ui.TableCell[Span.Title(r.CreatedAt.ToString("u"))[DashboardParts.Ago(r.CreatedAt, now)]],
                    // An expired key still reads until the sweep takes it.
                    Ui.TableCell[
                        r.ExpiresAt <= now
                            ? Ui.Badge.Sm["expired"]
                            : Span.Title(r.ExpiresAt.ToString("u"))[DashboardParts.Ago(r.ExpiresAt, now)]
                    ],
                    Ui.TableCell[r.SlidingSeconds is { } s ? DashboardParts.Duration(TimeSpan.FromSeconds(s)) : "—"],
                    evicts ? Ui.TableCell.Class("py-0")[Ui.Button.Sm.OnClick(() => EvictAsync(r.Key))["Evict"]] : null
                ])
            ]
        ];
    }

    private Component? FlushButton() =>
        options.Actions.HasFlag(RaskDashboardActions.Destructive) && _stats.Entries > 0
            ? Ui.Button.Danger.Icon(Ui.IconName.Trash).OnClick(() => Confirm(true))["Flush cache"]
            : null;

    private UiCallout? ConfirmPrompt() =>
        _confirmFlush
            ? Ui.Callout.Warning.Inline().Role("alert")
                .Heading($"Drop all {_stats.Entries} cache entries?")
                .Text("Nothing is lost permanently, but everything is recomputed at once.")
                .Actions([
                    Ui.Button.Danger.Sm.Key("confirm").OnClick(FlushAsync)["Confirm"],
                    Ui.Button.Sm.Key("cancel").OnClick(() => Confirm(false))["Cancel"]
                ])
            : null;

    private void Confirm(bool pending)
    {
        _confirmFlush = pending;
        StateHasChanged();
    }

    private async Task EvictAsync(string key)
    {
        var removed = await cache.Evict(key, CancellationToken).ConfigureAwait(false);
        Toast.Success(removed > 0 ? $"Evicted \"{key}\"." : $"\"{key}\" was already gone.");
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task FlushAsync()
    {
        _confirmFlush = false;
        Toast.Success($"Flushed {await cache.Flush(CancellationToken).ConfigureAwait(false)} entries.");
        _page = 0;
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task RefreshAsync()
    {
        await Load(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }

    private async Task GoAsync(int page)
    {
        _page = Math.Max(0, page);
        await Load(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }
}
