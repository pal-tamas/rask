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
            ActionResult(),
            Div.Class("mb-6 grid gap-4 sm:grid-cols-3")[
                OpsStat.Value(_stats.Entries.ToString()).Label("Entries").Icon(OpsIconName.Archive),
                OpsStat.Value(DashboardParts.Bytes(_stats.Bytes)).Label("Stored").Icon(OpsIconName.Database),
                OpsStat
                    .Value(_stats.Expired.ToString())
                    .Label("Expired, not yet swept")
                    .Icon(OpsIconName.Clock)
                    .Caption("removed by the purge sweep")
            ],
            _rows.Count == 0
                ? DashboardEmpty.Heading(Search is { Length: > 0 } ? $"No keys matching \"{Search}\"" : "Cache is empty")
                    .Detail("Entries appear here as soon as something is cached.")
                : KeyTable(now),
            Pager(),
            DashboardParked.Parked(IsParked).Resume(ResumeAsync),
        ];
    }

    private Component KeyTable(DateTime now) =>
        OpsTable[
            Thead.Class("border-b border-ops-line text-xs text-ops-muted")[
                Tr[
                    Th.Class("px-3 py-2 font-medium")["Key"],
                    Th.Class("px-3 py-2 font-medium")["Size"],
                    Th.Class("px-3 py-2 font-medium")["Written"],
                    Th.Class("px-3 py-2 font-medium")["Expires"],
                    Th.Class("px-3 py-2 font-medium")["Sliding"],
                    Th.Class("px-3 py-2")
                ]
            ],
            Tbody[_rows.Select(r => Tr.Key(r.Key).Class(r.ExpiresAt <= now
                ? "border-b border-ops-line/60 text-ops-muted last:border-0"
                : "border-b border-ops-line/60 last:border-0")[
                Td.Class("px-3 py-2")[
                    Div.Class($"max-w-[28rem] truncate {Ops.Mono}").Title(r.Key)[r.Key]
                ],
                Td.Class("px-3 py-2 tabular-nums")[DashboardParts.Bytes(r.Bytes)],
                Td.Class("px-3 py-2 text-xs text-ops-muted").Title(r.CreatedAt.ToString("u"))[
                    DashboardParts.Ago(r.CreatedAt, now)
                ],
                Td.Class("px-3 py-2 text-xs").Title(r.ExpiresAt.ToString("u"))[
                    r.ExpiresAt <= now
                        ? OpsBadge.Label("expired")
                        : Span.Class("text-ops-muted")[DashboardParts.Ago(r.ExpiresAt, now)]
                ],
                Td.Class("px-3 py-2 text-xs text-ops-muted")[
                    r.SlidingSeconds is { } s ? DashboardParts.Duration(TimeSpan.FromSeconds(s)) : "—"
                ],
                Td.Class("px-3 py-2 text-right")[EvictButton(r.Key)]
            ])]
        ];

    private Component? Pager()
    {
        var pages = (int)Math.Ceiling(_total / (double)options.PageSize);
        if (pages <= 1)
        {
            return null;
        }

        return Div.Class("mt-4 flex items-center gap-3")[
            Button.Type("button").Class(Ops.Button)
                .Disabled(_page == 0)
                .OnClickAsync(() => GoAsync(_page - 1))["Previous"],
            Span.Class("text-xs text-ops-muted")[$"Page {_page + 1} of {pages} — {_total} keys"],
            Button.Type("button").Class(Ops.Button)
                .Disabled(_page >= pages - 1)
                .OnClickAsync(() => GoAsync(_page + 1))["Next"]
        ];
    }

    // Evicting one key is a recompute, not a lost fact, so it sits in the Safe tier and needs no
    // confirmation. Flushing everything is correctness-safe too, but a cold cache on a busy app means a
    // stampede — hence the Destructive tier and a confirmation.
    private Component? EvictButton(string key) =>
        options.Actions.HasFlag(RaskDashboardActions.Safe)
            ? Button.Type("button").Class(Ops.Button).OnClickAsync(() => EvictAsync(key))["Evict"]
            : null;

    private Component? FlushButton() =>
        options.Actions.HasFlag(RaskDashboardActions.Destructive) && _stats.Entries > 0
            ? Button.Type("button")
                .Class("inline-flex items-center rounded-md bg-red-500/15 px-2.5 py-1.5 text-xs font-medium text-red-300 hover:bg-red-500/25")
                .OnClick(() => Confirm(true))["Flush cache"]
            : null;

    private Component? ActionResult()
    {
        if (_confirmFlush)
        {
            return OpsNotice.Tone("warn")[
                Span.Class("grow")[
                    $"Drop all {_stats.Entries} cache entries? Nothing is lost permanently, but everything is recomputed at once."
                ],
                Button.Type("button")
                    .Class("inline-flex items-center rounded-md bg-red-500/20 px-2.5 py-1.5 text-xs font-medium text-red-200 hover:bg-red-500/30")
                    .OnClickAsync(FlushAsync)["Confirm"],
                Button.Type("button").Class(Ops.Button).OnClick(() => Confirm(false))["Cancel"]
            ];
        }

        return _message is { } message
            ? OpsNotice.Tone("info")[
                Span.Class("grow")[message],
                Button.Type("button")
                    .Class("rounded-md px-2 py-1 text-xs text-ops-muted hover:text-ops-ink")
                    .Aria(new Dictionary<string, string?> { ["label"] = "Dismiss" })
                    .OnClick(Dismiss)["Dismiss"]
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
