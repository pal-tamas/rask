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
            OpsHeader.Heading("Cache").Actions(FlushButton()),
            DashboardError.Message(LoadError),
            ConfirmPrompt(),
            Div.Class("mb-4 sm:mb-5")[
                OpsMetricRow.Columns(3)[
                    OpsMetric.Key("entries").Label("Entries").Value(_stats.Entries.ToString()),
                    OpsMetric.Key("stored").Label("Stored").Value(DashboardParts.Bytes(_stats.Bytes)),
                    OpsMetric
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
                OpsSearch
                    .Placeholder("Search keys")
                    .Label("Search cache keys")
                    .Value(Search)
                    .OnSearch(SearchAsync)
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

    private async Task SearchAsync(string value)
    {
        Search = string.IsNullOrWhiteSpace(value) ? null : value;
        _page = 0;
        await LoadAsync(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }

    private Component KeyTable(DateTime now) =>
        OpsTable[
            Thead.Class("border-b border-ops-line text-xs text-ops-muted")[
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
                ? "border-b border-ops-line/60 text-ops-muted last:border-0"
                : "border-b border-ops-line/60 last:border-0")[
                Td.Class("w-full max-w-0 px-3 py-2 align-top")[
                    Div.Class("min-w-0")[
                        Div.Class($"truncate sm:max-w-[28rem] {Ops.Mono}").Title(r.Key)[r.Key],
                        // Size and expiry follow the key down when their own columns are gone.
                        Div.Class("mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-ops-muted sm:hidden")[
                            Span.Class("tabular-nums")[DashboardParts.Bytes(r.Bytes)],
                            r.ExpiresAt <= now
                                ? OpsBadge.Label("expired")
                                : Span.Title(r.ExpiresAt.ToString("u"))[
                                    $"expires {DashboardParts.Ago(r.ExpiresAt, now)}"
                                ]
                        ]
                    ]
                ],
                Td.Class("hidden px-3 py-2 align-top tabular-nums sm:table-cell")[DashboardParts.Bytes(r.Bytes)],
                Td.Class("hidden px-3 py-2 align-top text-xs text-ops-muted md:table-cell")
                    .Title(r.CreatedAt.ToString("u"))[
                    DashboardParts.Ago(r.CreatedAt, now)
                ],
                Td.Class("hidden px-3 py-2 align-top text-xs sm:table-cell").Title(r.ExpiresAt.ToString("u"))[
                    r.ExpiresAt <= now
                        ? OpsBadge.Label("expired")
                        : Span.Class("text-ops-muted")[DashboardParts.Ago(r.ExpiresAt, now)]
                ],
                Td.Class("hidden px-3 py-2 align-top text-xs text-ops-muted lg:table-cell")[
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
            OpsButton.Key("prev").Label("Previous")
                .Disabled(_page == 0)
                .OnClickAsync(() => GoAsync(_page - 1)),
            Span.Class("text-center text-xs text-ops-muted")[
                Span[$"Page {_page + 1} of {pages}"],
                Span.Class("hidden sm:inline")[$" — {_total} keys"]
            ],
            OpsButton.Key("next").Label("Next")
                .Disabled(_page >= pages - 1)
                .OnClickAsync(() => GoAsync(_page + 1))
        ];
    }

    // Evicting one key is a recompute, not a lost fact, so it sits in the Safe tier and needs no
    // confirmation. Flushing everything is correctness-safe too, but a cold cache on a busy app means a
    // stampede — hence the Destructive tier and a confirmation.
    private Component? EvictButton(string key) =>
        options.Actions.HasFlag(RaskDashboardActions.Safe)
            ? OpsButton.Label("Evict").OnClickAsync(() => EvictAsync(key))
            : null;

    private Component? FlushButton() =>
        options.Actions.HasFlag(RaskDashboardActions.Destructive) && _stats.Entries > 0
            ? OpsButton.Label("Flush cache").Tone("danger").Icon(OpsIconName.Trash).OnClick(() => Confirm(true))
            : null;

    private Component? ConfirmPrompt() =>
        _confirmFlush
            ? OpsNotice.Tone("warn")[
                Span.Class("min-w-0 grow break-words")[
                    $"Drop all {_stats.Entries} cache entries? Nothing is lost permanently, but everything is recomputed at once."
                ],
                OpsButton.Key("confirm").Label("Confirm").Tone("danger").OnClickAsync(FlushAsync),
                OpsButton.Key("cancel").Label("Cancel").OnClick(() => Confirm(false))
            ]
            : null;

    private Component? ResultToast() =>
        _message is { } message ? OpsToast.Message(message).Dismiss(Dismiss) : null;

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
