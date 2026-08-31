using Microsoft.Extensions.Caching.Distributed;
using Rask.Cache;
using Rask.Core.Routing;

namespace Rask.Example.EfCore.Features.Cache;

// Vertical slice: a read-through cache on the app's own SQLite database. GetOrCreateAsync runs the
// "expensive" factory only on a miss and stores the result as a CacheEntry row; a second load within the
// sliding window is served straight from the DB — no recompute, no Redis. "Clear" removes the entry so the
// next load recomputes, proving the cache is what's serving the repeated reads.
[Route("cache")]
public sealed partial class CacheReportPage(ICache cache) : Component
{
    private const string CacheKey = "cache:demo:report";

    private Report? _report;
    private bool _servedFromCache;

    protected override Component? HeadAssets => Title["Cache — Rask EF Core"];

    private async Task LoadAsync()
    {
        var factoryRan = false;
        _report = await cache.GetOrCreateAsync(
            CacheKey,
            async ct =>
            {
                factoryRan = true;
                // Stand in for an expensive computation or a slow upstream call.
                await Task.Delay(400, ct);
                return new Report($"Report #{Random.Shared.Next(1000, 9999)}", DateTime.UtcNow);
            },
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(2) });

        _servedFromCache = !factoryRan;
    }

    private async Task ClearAsync()
    {
        await cache.RemoveAsync(CacheKey);
        _report = null;
        _servedFromCache = false;
    }

    protected override Component? Render() =>
        Div.Class("rounded-xl bg-white shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:ring-slate-700 border-0 mx-auto").Style("max-width: 32rem")[
            Div.Class("p-5")[
                H1.Class("text-xl font-semibold mb-3")["Database-backed cache"],
                P.Class("text-slate-500 dark:text-slate-400 text-sm")[
                    "The first load runs an \"expensive\" factory and stores the result as a row on the app's ",
                    "own SQLite database. Load again and it's served from that row — no recompute. ",
                    "Clear it to force the next load to recompute."
                ],
                _report is { } report
                    ? Div.Class("rounded-lg px-4 py-3 text-sm bg-slate-100 text-slate-800 dark:bg-slate-800 dark:text-slate-200" + (_servedFromCache ? "rounded-lg px-4 py-3 text-sm bg-sky-50 text-sky-900 dark:bg-sky-950 dark:text-sky-200" : "rounded-lg px-4 py-3 text-sm bg-emerald-50 text-emerald-900 dark:bg-emerald-950 dark:text-emerald-200")).Id("cache-result")[
                        Div[Strong["Value: "], Span.Id("cache-value")[report.Label]],
                        Div.Class("text-sm")[
                            "Computed at ", Span.Id("cache-computed-at")[report.ComputedAt.ToString("HH:mm:ss.fff")]
                        ],
                        Div.Class("text-sm mt-1")[
                            Span.Id("cache-source")[_servedFromCache ? "Served from cache" : "Computed fresh"]
                        ]
                    ]
                    : null,
                Div.Class("flex gap-2 pt-2")[
                    Button.Type("button").Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-violet-600 text-white hover:bg-violet-500").Id("cache-load").OnClickAsync(LoadAsync)[
                        Span.Class("me-1").Attributes(("aria-hidden", "true"))["⚡"], "Load report"
                    ],
                    Button
                        .Type("button")
                        .Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-transparent ring-1 text-slate-700 ring-slate-300 hover:bg-slate-50 dark:text-slate-300 dark:ring-slate-600 dark:hover:bg-slate-800")
                        .Id("cache-clear")
                        .OnClickAsync(ClearAsync)[
                        Span.Class("me-1").Attributes(("aria-hidden", "true"))["🗑"], "Clear cache"
                    ]
                ]
            ]
        ];
}

// The cached payload: a label plus when it was computed, so a repeated load can show the value is unchanged.
public sealed record Report(string Label, DateTime ComputedAt);
