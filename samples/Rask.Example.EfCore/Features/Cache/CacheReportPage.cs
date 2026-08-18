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
        Div.Class("card shadow-sm border-0 mx-auto").Style("max-width: 32rem")[
            Div.Class("card-body")[
                H1.Class("h4 mb-3")["Database-backed cache"],
                P.Class("text-secondary small")[
                    "The first load runs an \"expensive\" factory and stores the result as a row on the app's ",
                    "own SQLite database. Load again and it's served from that row — no recompute. ",
                    "Clear it to force the next load to recompute."
                ],
                _report is { } report
                    ? Div.Class("alert " + (_servedFromCache ? "alert-info" : "alert-success")).Id("cache-result")[
                        Div[Strong["Value: "], Span.Id("cache-value")[report.Label]],
                        Div.Class("small")[
                            "Computed at ", Span.Id("cache-computed-at")[report.ComputedAt.ToString("HH:mm:ss.fff")]
                        ],
                        Div.Class("small mt-1")[
                            Span.Id("cache-source")[_servedFromCache ? "Served from cache" : "Computed fresh"]
                        ]
                    ]
                    : null,
                Div.Class("d-flex gap-2 pt-2")[
                    Button.Type("button").Class("btn btn-primary").Id("cache-load").OnClickAsync(LoadAsync)[
                        I.Class("bi bi-lightning-charge me-1"), "Load report"
                    ],
                    Button
                        .Type("button")
                        .Class("btn btn-outline-secondary")
                        .Id("cache-clear")
                        .OnClickAsync(ClearAsync)[
                        I.Class("bi bi-trash me-1"), "Clear cache"
                    ]
                ]
            ]
        ];
}

// The cached payload: a label plus when it was computed, so a repeated load can show the value is unchanged.
public sealed record Report(string Label, DateTime ComputedAt);
