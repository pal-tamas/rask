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
            return DashboardEmpty.Heading("Cache isn't registered")
                .Detail("Call AddRaskCache<TContext>() and modelBuilder.AddRaskCache() to see cache entries here.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return [
            UiHeader.Heading("Cache").Actions(FlushButton()),
            DashboardError.Message(LoadError),
            ConfirmPrompt(),
            Div.Class("mb-4 sm:mb-5")[
                UiMetricRow.Columns(3)[
                    UiMetric.Key("entries").Label("Entries").Value(_stats.Entries.ToString()),
                    UiMetric.Key("stored").Label("Stored").Value(DashboardParts.Bytes(_stats.Bytes)),
                    UiMetric
                        .Key("expired")
                        .Label("Expired, not swept")
                        .Value(_stats.Expired.ToString())
                        .Caption("removed by the purge sweep")
                ]
            ],
            // The q filter has always been here — the empty state below has named it since this page
            // shipped — but nothing ever rendered a box to type it into, so it was reachable only by
            // hand-editing the URL.
            Div.Class("mb-4")[
                UiSearch
                    .Value(Search)
                    .Placeholder("Search keys")
                    .AccessibleLabel("Search cache keys")
                    .OnChangeAsync(SearchAsync)
            ],
            _rows.Count == 0
                ? DashboardEmpty.Heading(Search is { Length: > 0 } ? $"No keys matching \"{Search}\"" : "Cache is empty")
                    .Detail("Entries appear here as soon as something is cached.")
                : KeyTable(now),
            Pager(),
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

    private Component KeyTable(DateTime now) =>
        UiTable[
            Thead.Class("border-b border-ui-line text-xs text-ui-muted")[
                Tr[
                    Th.Class("px-3 py-2 font-medium")["Key"],
                    Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["Size"],
                    Th.Class("hidden px-3 py-2 font-medium md:table-cell")["Written"],
                    Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["Expires"],
                    Th.Class("hidden px-3 py-2 font-medium lg:table-cell")["Sliding"],
                    Th.Class("px-3 py-2")
                ]
            ],
            Tbody[_rows.Select(r => Tr.Key(r.Key).Class(r.ExpiresAt <= now
                ? "border-b border-ui-line/60 text-ui-muted last:border-0"
                : "border-b border-ui-line/60 last:border-0")[
                Td.Class("w-full max-w-0 px-3 py-2 align-top")[
                    Div.Class("min-w-0")[
                        Div.Class($"truncate sm:max-w-[28rem] {UiStyles.Mono}").Title(r.Key)[r.Key],
                        // Size and expiry follow the key down when their own columns are gone.
                        Div.Class("mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-ui-muted sm:hidden")[
                            Span.Class("tabular-nums")[DashboardParts.Bytes(r.Bytes)],
                            r.ExpiresAt <= now
                                ? UiBadge.Label("expired")
                                : Span.Title(r.ExpiresAt.ToString("u"))[
                                    $"expires {DashboardParts.Ago(r.ExpiresAt, now)}"
                                ]
                        ]
                    ]
                ],
                Td.Class("hidden whitespace-nowrap px-3 py-2 align-top tabular-nums sm:table-cell")[DashboardParts.Bytes(r.Bytes)],
                Td.Class("hidden whitespace-nowrap px-3 py-2 align-top text-xs text-ui-muted md:table-cell")
                    .Title(r.CreatedAt.ToString("u"))[
                    DashboardParts.Ago(r.CreatedAt, now)
                ],
                Td.Class("hidden whitespace-nowrap px-3 py-2 align-top text-xs sm:table-cell").Title(r.ExpiresAt.ToString("u"))[
                    r.ExpiresAt <= now
                        ? UiBadge.Label("expired")
                        : Span.Class("text-ui-muted")[DashboardParts.Ago(r.ExpiresAt, now)]
                ],
                Td.Class("hidden whitespace-nowrap px-3 py-2 align-top text-xs text-ui-muted lg:table-cell")[
                    r.SlidingSeconds is { } s ? DashboardParts.Duration(TimeSpan.FromSeconds(s)) : "—"
                ],
                Td.Class("px-3 py-2 align-top text-right")[EvictButton(r.Key)]
            ])]
        ];

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
            UiButton.Key("prev")
                .Disabled(_page == 0)
                .OnClickAsync(() => GoAsync(_page - 1))["Previous"],
            Span.Class("text-center text-xs text-ui-muted")[
                Span[$"Page {_page + 1} of {pages}"],
                Span.Class("hidden sm:inline")[$" — {_total} keys"]
            ],
            UiButton.Key("next")
                .Disabled(_page >= pages - 1)
                .OnClickAsync(() => GoAsync(_page + 1))["Next"]
        ];
    }

    // Evicting one key is a recompute, not a lost fact, so it sits in the Safe tier and needs no
    // confirmation. Flushing everything is correctness-safe too, but a cold cache on a busy app means a
    // stampede — hence the Destructive tier and a confirmation.
    private Component? EvictButton(string key) =>
        options.Actions.HasFlag(RaskDashboardActions.Safe)
            ? UiButton.OnClickAsync(() => EvictAsync(key))["Evict"]
            : null;

    private Component? FlushButton() =>
        options.Actions.HasFlag(RaskDashboardActions.Destructive) && _stats.Entries > 0
            ? UiButton.Tone(UiTone.Error).OnClick(() => Confirm(true))[
                UiIcon.Name(UiIconName.Trash).Class("size-4 shrink-0"), "Flush cache"]
            : null;

    private Component? ConfirmPrompt() =>
        _confirmFlush
            ? UiNotice.Tone("warn")[
                Span.Class("min-w-0 grow break-words")[
                    $"Drop all {_stats.Entries} cache entries? Nothing is lost permanently, but everything is recomputed at once."
                ],
                UiButton.Key("confirm").Tone(UiTone.Error).OnClickAsync(FlushAsync)["Confirm"],
                UiButton.Key("cancel").OnClick(() => Confirm(false))["Cancel"]
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
