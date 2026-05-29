using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Example.Shared.Demos;

// Lifecycle showcase with a simulated price feed. Each hook has a natural job:
//
//   * OnMount             — synchronous local init (record mount time).
//   * OnMountAsync        — re-hydrate persisted history from sessionStorage,
//                           then run the poll loop. Each await yields to the
//                           framework's LifecycleSyncContext, which re-renders.
//   * OnPropsChanged*     — react to the [RouteParam] Symbol changing (the
//                           parent page binds Symbol from the URL).
//   * OnRendered          — record first-paint latency on the inaugural render.
//   * OnRenderedAsync     — push the rolling buffer into Chart.js via the
//                           sibling LiveTicker.js. Idempotent; the publish-
//                           render mechanism keeps the unguarded await safe.
//   * OnUnmount*          — log teardown. The framework's CancellationToken
//                           is already firing here, which is what cancels the
//                           in-flight Task.Delay in the poll loop above.
//
// The poll loop is fully synthetic — no external HTTP. Real public crypto
// APIs (CoinCap, CoinGecko, Coinbase) all rate-limit or 403 server-to-server
// traffic, which made the demo flaky. A local random-walk price source
// preserves the lifecycle/async story end-to-end (the loop still yields on
// every Task.Delay, the CancellationToken still cancels it on unmount) but
// is deterministic and offline-safe.
public sealed class LiveTicker(IJSRuntime js) : Component
{
    // ~3 min of points at the default 3 s poll. Bounded so a long-running tab
    // doesn't grow the sessionStorage entry indefinitely.
    private const int HistoryCapacity = 60;
    private const string StorageKeyPrefix = "rask-live-ticker:";

    private readonly List<PricePoint> _history = new();
    private string? _error;
    private DateTimeOffset? _firstPaintAt;
    private string? _lastSymbol;
    private DateTimeOffset _mountedAt;

    // Required factory parameter — properties with an initializer are excluded
    // by the generator, but we want callers to pass Symbol explicitly. Same
    // pattern as CodeSample.Source (CodeSample.cs:17).
#pragma warning disable CS8618
    public string Symbol { get; set; }
#pragma warning restore CS8618

    // Nullable so the generator emits Interval as an optional factory parameter
    // (default null). Callers — production pages and unit tests alike — pass an
    // explicit value when they want one; the default 3 s lives next to the read
    // site below.
    public int? Interval { get; set; }

    private int IntervalMs => Interval ?? 3000;

    // Parent-owned log sink so OnUnmount* entries survive disposal — same pattern
    // LifecycleCycleProbe uses (LifecycleProbe.cs:49).
    public Action<string>? Log { get; set; }

    // The framework dedupes head-asset entries by full rendered HTML, so two
    // LiveTicker instances on the same page share a single Chart.js script tag.
    protected override RenderResult Head =>
        Script(LiveOptions.PathBase + "/lib/chartjs/chart.umd.js");

    protected override void OnMount()
    {
        _mountedAt = DateTimeOffset.UtcNow;
        Emit($"OnMount: requesting persisted history for {Symbol}");
    }

    protected override async Task OnMountAsync()
    {
        await LoadFromStorageAsync();
        Emit($"OnMountAsync: loaded {_history.Count} persisted points; starting poll loop");

        var ct = CancellationToken;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await PollOnceAsync(ct);
                await Task.Delay(IntervalMs, ct).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on unmount — fall through to the exit log.
        }

        Emit("OnMountAsync: poll loop cancelled");
    }

    protected override void OnPropsChanged()
    {
        if (_lastSymbol is null)
        {
            Emit($"OnPropsChanged: initial Symbol={Symbol}");
        }
        else if (_lastSymbol != Symbol)
        {
            Emit($"OnPropsChanged: Symbol {_lastSymbol} → {Symbol}");
        }
    }

    protected override async Task OnPropsChangedAsync()
    {
        if (_lastSymbol is not null && _lastSymbol != Symbol)
        {
            _history.Clear();
            _error = null;
            await LoadFromStorageAsync();
            Emit($"OnPropsChangedAsync: switched to {Symbol}, loaded {_history.Count} persisted points");
        }

        _lastSymbol = Symbol;
    }

    protected override void OnRendered(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _firstPaintAt = DateTimeOffset.UtcNow;
        var latency = (_firstPaintAt.Value - _mountedAt).TotalMilliseconds;
        Emit($"OnRendered(firstRender:true): first paint {latency:F0} ms after OnMount");
    }

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        await js.InvokeVoidAsync("Rask.LiveTicker.draw", _history.ToArray());
        if (firstRender)
        {
            Emit("OnRenderedAsync(firstRender:true): Chart.js initialised");
        }
    }

    protected override void OnUnmount() => Emit("OnUnmount: stopping (sync)");

    protected override async Task OnUnmountAsync()
    {
        // Stand-in for a real "POST /stats/session-end". Demonstrates that the
        // framework awaits async unmount work on the IAsyncDisposable path.
        await Task.Delay(50);
        Emit("OnUnmountAsync: flushed (after 50ms)");
    }

    protected override RenderResult Render()
    {
        var current = _history.Count > 0 ? _history[^1].PriceUsd : 0m;
        var first = _history.Count > 0 ? _history[0].PriceUsd : 0m;
        var change = first == 0m ? 0m : (current - first) / first * 100m;
        var changeClass = change >= 0 ? "text-success" : "text-danger";
        var changeSign = change >= 0 ? "+" : string.Empty;

        return Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Div(Class: "d-flex align-items-baseline justify-content-between mb-3")[
                    H3(Class: "h4 mb-0 fw-semibold", Id: "ticker-symbol")[Symbol],
                    Span(Class: "text-secondary small")[$"poll {IntervalMs} ms · {_history.Count}/{HistoryCapacity} pts"]
                ],
                Div(Class: "d-flex align-items-baseline gap-3 mb-3")[
                    _history.Count == 0
                        ? (Child)Span(Class: "fs-3 text-secondary", Id: "ticker-price")["Waiting for first tick…"]
                        : (Child)Span(Class: "fs-2 fw-bold", Id: "ticker-price")[
                            $"${current.ToString("N2", CultureInfo.InvariantCulture)}"],
                    _history.Count > 1
                        ? Span(Class: $"fs-6 fw-semibold {changeClass}", Id: "ticker-change")[
                            $"{changeSign}{change.ToString("F2", CultureInfo.InvariantCulture)}% since first sample"]
                        : Fragment()
                ],
                _error is null
                    ? Fragment()
                    : Div(Class: "alert alert-warning py-2 px-3 small mb-3", Id: "ticker-error")[
                        I(Class: "bi bi-exclamation-triangle me-2"), $"Feed error: {_error}"
                    ],
                // Wrapping the canvas in a fixed-height container lets Chart.js
                // resize against a known box even before the first draw.
                Div(Class: "ticker-chart-container", Style: "position: relative; height: 160px;")[
                    Canvas(
                        Id: "ticker-chart",
                        Data: new Dictionary<string, string?> { ["rask-ticker"] = null })
                ]
            ]
        ];
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var symbolForRequest = Symbol;
        try
        {
            // Pretend network latency so the lifecycle/cancellation story stays
            // faithful: the loop yields here, the CancellationToken cancels the
            // Task.Delay on unmount, and the post-await continuation re-renders.
            await Task.Delay(50, ct).ConfigureAwait(true);

            // Symbol may have changed during the simulated latency; drop the
            // result so the chart isn't contaminated with the previous asset's data.
            if (symbolForRequest != Symbol)
            {
                return;
            }

            var price = SimulateNextPrice(symbolForRequest);
            _history.Add(new PricePoint(DateTimeOffset.UtcNow, price));
            if (_history.Count > HistoryCapacity)
            {
                _history.RemoveAt(0);
            }

            _error = null;
            await PersistAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    // Random-walk price source: each tick drifts the last value by ~+/- 0.4%.
    // Seeded per symbol so BTC / ETH / SOL show recognisable magnitudes.
    private decimal SimulateNextPrice(string symbol)
    {
        var previous = _history.Count > 0 ? _history[^1].PriceUsd : SeedPrice(symbol);
        var step = (decimal)(Random.Shared.NextDouble() - 0.5) * 0.008m;
        return Math.Round(previous * (1m + step), 2);
    }

    private static decimal SeedPrice(string symbol) => symbol switch
    {
        "BTC" => 70_000m,
        "ETH" => 2_500m,
        "SOL" => 150m,
        _ => 100m
    };

    private async Task LoadFromStorageAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey).ConfigureAwait(true);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var arr = JsonSerializer.Deserialize(json, LiveTickerJsonContext.Default.PricePointArray);
            if (arr is null)
            {
                return;
            }

            _history.Clear();
            _history.AddRange(arr);
        }
        catch
        {
            // Bad JSON / quota / cleared — start fresh.
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_history.ToArray(), LiveTickerJsonContext.Default.PricePointArray);
            await js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, json).ConfigureAwait(true);
        }
        catch
        {
            // Best-effort persistence; loss is acceptable for a demo.
        }
    }

    private string StorageKey => StorageKeyPrefix + Symbol;

    private void Emit(string entry) => Log?.Invoke(entry);
}

public readonly record struct PricePoint(DateTimeOffset Timestamp, decimal PriceUsd);

[JsonSerializable(typeof(PricePoint[]))]
internal sealed partial class LiveTickerJsonContext : JsonSerializerContext;
