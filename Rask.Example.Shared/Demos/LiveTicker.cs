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
//                           then run the poll loop. The loop awaits with
//                           ConfigureAwait(false) so it does NOT lean on the
//                           framework's auto-render-per-await; instead it calls
//                           StateHasChanged() exactly once per real data change
//                           (one render, one chart redraw per tick — see the
//                           render-discipline note below).
//   * OnPropsChanged*     — react to the [RouteParam] Symbol changing (the
//                           parent page binds Symbol from the URL). The async
//                           half also wakes the poll loop (_wake.Cancel()) so the
//                           new symbol shows a price within ~50 ms instead of
//                           waiting out the running inter-tick delay.
//   * OnRendered          — record first-paint latency on the inaugural render.
//   * OnRenderedAsync     — push the rolling buffer into Chart.js via the sibling
//                           LiveTicker.js, but only when the buffer actually
//                           changed since the last draw (version-gated, so a
//                           no-op publish render doesn't re-marshal the array).
//   * OnUnmount*          — log teardown. The framework's CancellationToken is
//                           already firing here, which cancels both the in-flight
//                           Task.Delay and the interruptible inter-tick delay.
//
// Render discipline: every await in the loop uses ConfigureAwait(false), so the
// LifecycleSyncContext never auto-renders mid-loop; the single StateHasChanged()
// after a point is appended is the one render per tick. Off-context mutation is
// safe here — server renders run under the session lock and ticks are strictly
// sequential, so the buffer is never mutated during a render; WASM is
// single-threaded; and StateHasChanged() is a no-op once the component unmounts.
//
// The poll loop is fully synthetic — no external HTTP. Real public crypto
// APIs (CoinCap, CoinGecko, Coinbase) all rate-limit or 403 server-to-server
// traffic, which made the demo flaky. A local random-walk price source
// preserves the lifecycle/async story end-to-end (the loop still yields on
// every Task.Delay, the CancellationToken still cancels it on unmount) but
// is deterministic and offline-safe.
public sealed class LiveTicker(IJSRuntime js) : Component
{
    // ~1 min of points at the default 1 s poll. Bounded so a long-running tab
    // doesn't grow the sessionStorage entry indefinitely.
    private const int HistoryCapacity = 60;
    private const string StorageKeyPrefix = "rask-live-ticker:";

    private readonly List<PricePoint> _history = new();
    private string? _error;
    private DateTimeOffset? _firstPaintAt;
    private string? _lastSymbol;
    private DateTimeOffset _mountedAt;

    // Recreated each loop iteration and linked to the lifetime CancellationToken.
    // OnPropsChangedAsync cancels it to wake the inter-tick delay early so a symbol
    // switch polls the new asset immediately instead of waiting out IntervalMs.
    private CancellationTokenSource? _wake;

    // Monotonic buffer version (++ on every _history mutation) vs the version last
    // pushed to Chart.js. Lets OnRenderedAsync skip the JS round-trip on renders
    // that didn't change the data (e.g. a publish-only re-render).
    private int _version;
    private int _lastDrawnVersion = -1;

    // Required factory parameter — properties with an initializer are excluded
    // by the generator, but we want callers to pass Symbol explicitly. Same
    // pattern as CodeSample.Source (CodeSample.cs:17).
#pragma warning disable CS8618
    public string Symbol { get; set; }
#pragma warning restore CS8618

    // Nullable so the generator emits Interval as an optional factory parameter
    // (default null). Callers — production pages and unit tests alike — pass an
    // explicit value when they want one; the default 1 s lives next to the read
    // site below (denser points ⇒ a smoother-looking line).
    public int? Interval { get; set; }

    private int IntervalMs => Interval ?? 1000;

    // Parent-owned log sink so OnUnmount* entries survive disposal — same pattern
    // LifecycleCycleProbe uses (LifecycleProbe.cs:49).
    public Action<string>? Log { get; set; }

    // Pluggable price feed. Null ⇒ the synthetic random-walk below. A real deploy
    // swaps in an HTTP-backed Func here — the "one-line change in PollOnceAsync"
    // the page narrative describes. A source that throws is caught in PollOnceAsync
    // and surfaced via _error (the #ticker-error alert).
    public Func<string, decimal>? PriceSource { get; set; }

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
        await LoadFromStorageAsync().ConfigureAwait(false);
        StateHasChanged();
        Emit($"OnMountAsync: loaded {_history.Count} persisted points; starting poll loop");

        var ct = CancellationToken;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Publish this iteration's wake BEFORE polling so a Symbol switch
                // landing mid-poll still registers: PollOnceAsync drops the stale
                // result, then the already-cancelled wake makes the inter-tick delay
                // return immediately and the loop re-polls the new asset. Linked to ct
                // so unmount tears the loop down. Exchange-before-Dispose mirrors
                // Virtualize's superseded-CTS handling so a racing Cancel() is benign.
                var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Interlocked.Exchange(ref _wake, wake)?.Dispose();

                await PollOnceAsync(ct).ConfigureAwait(false);

                try
                {
                    await Task.Delay(IntervalMs, wake.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Woken by a Symbol switch — fall through and poll immediately.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on unmount — fall through to the exit log.
        }
        finally
        {
            Interlocked.Exchange(ref _wake, null)?.Dispose();
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
            _version++;
            _error = null;
            // Keep ConfigureAwait(true) here: this is a distinct hook invocation with
            // its own fresh LifecycleSyncContext, so the auto-render paints the new
            // symbol's (empty/hydrated) state. It is not poisoned by the loop's
            // ConfigureAwait(false) chain — that runs on a separate invocation.
            await LoadFromStorageAsync().ConfigureAwait(true);
            Emit($"OnPropsChangedAsync: switched to {Symbol}, loaded {_history.Count} persisted points");

            // Wake the poll loop AFTER the buffer reset so the immediate poll sees the
            // correct state. The Exchange-before-Dispose ordering in the loop makes a
            // cancel-vs-recreate race benign; swallow the rare disposed-CTS case.
            try
            {
                Volatile.Read(ref _wake)?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
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
        // Skip the JS round-trip when the buffer is unchanged since the last draw
        // (e.g. a publish-only re-render). Set _lastDrawnVersion before the await so
        // a re-entrant render during the in-flight call can't double-dispatch.
        if (_version == _lastDrawnVersion)
        {
            return;
        }

        _lastDrawnVersion = _version;
        await js.InvokeVoidAsync("Rask.LiveTicker.draw", _history.ToArray()).ConfigureAwait(false);
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
            // faithful: the loop yields here and the CancellationToken cancels the
            // Task.Delay on unmount. ConfigureAwait(false) — the explicit
            // StateHasChanged() below is the one render per tick.
            await Task.Delay(50, ct).ConfigureAwait(false);

            // Symbol may have changed during the simulated latency; drop the
            // result so the chart isn't contaminated with the previous asset's data.
            if (symbolForRequest != Symbol)
            {
                return;
            }

            var price = (PriceSource ?? SimulateNextPrice)(symbolForRequest);
            _history.Add(new PricePoint(DateTimeOffset.UtcNow, price));
            if (_history.Count > HistoryCapacity)
            {
                _history.RemoveAt(0);
            }

            _version++;
            _error = null;
            StateHasChanged();                      // the one render (+ one redraw) per tick
            await PersistAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            StateHasChanged();                      // surface the #ticker-error alert
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
            var json = await js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey).ConfigureAwait(false);
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
            _version++;
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
            await js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, json).ConfigureAwait(false);
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
