using System.Globalization;

namespace Rask.Example.Shared.Features;

// Lifecycle showcase with a simulated price feed. Each hook has a natural job:
//
//   * OnMount             — synchronous local init (record mount time).
//   * OnMountAsync        — run the poll loop. The loop awaits with
//                           ConfigureAwait(false) so it does NOT lean on the
//                           framework's auto-render-per-await; instead it calls
//                           StateHasChanged() exactly once per real data change
//                           (one render per tick — see the render-discipline note
//                           below).
//   * OnPropsChanged*     — react to the [RouteParam] Symbol changing (the
//                           parent page binds Symbol from the URL). The async
//                           half also wakes the poll loop (_wake.Cancel()) so the
//                           new symbol shows a price within ~50 ms instead of
//                           waiting out the running inter-tick delay.
//   * OnRendered          — record first-paint latency on the inaugural render.
//   * OnUnmount*          — log teardown. The framework's CancellationToken is
//                           already firing here, which cancels both the in-flight
//                           Task.Delay and the interruptible inter-tick delay.
//
// No JavaScript: the price history is drawn as a server-rendered SVG <Sparkline>
// straight from Render(), so there is no Chart.js, no canvas, no scoped JS, and no
// IJSRuntime round-trip. A live re-render simply re-emits the updated <svg> over the
// same transport as the rest of the page — which is why this component has no
// OnRenderedAsync hook: the chart is part of normal render output, not a post-render
// side effect.
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
public sealed partial class LiveTicker : Component
{
    // ~1 min of points at the default 1 s poll. Bounded so a long-running tab
    // doesn't grow the rolling buffer indefinitely.
    private const int HistoryCapacity = 60;

    private readonly List<PricePoint> _history = new();
    private string? _error;
    private DateTimeOffset? _firstPaintAt;
    private string? _lastSymbol;
    private DateTimeOffset _mountedAt;

    // Recreated each loop iteration and linked to the lifetime CancellationToken.
    // OnPropsChangedAsync cancels it to wake the inter-tick delay early so a symbol
    // switch polls the new asset immediately instead of waiting out IntervalMs.
    private CancellationTokenSource? _wake;

    // Required factory parameter — properties with an initializer are excluded
    // by the generator, but we want callers to pass Symbol explicitly. LiveTicker
    // has no DI constructor (unlike CodeSample), so we mark it `required` for
    // language-level enforcement — no CS8618 suppression and no RASK002.
    public new required string Symbol { get; set; }

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

    protected override void OnMount()
    {
        _mountedAt = DateTimeOffset.UtcNow;
        Emit($"OnMount: starting ticker for {Symbol}");
    }

    protected override async Task OnMountAsync()
    {
        Emit("OnMountAsync: starting poll loop");

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
                // VirtualizeModel's superseded-CTS handling so a racing Cancel() is benign.
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

    protected override Task OnPropsChangedAsync()
    {
        if (_lastSymbol is not null && _lastSymbol != Symbol)
        {
            _history.Clear();
            _error = null;
            Emit($"OnPropsChangedAsync: switched to {Symbol}, cleared history");

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
        return Task.CompletedTask;
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

    protected override void OnUnmount() => Emit("OnUnmount: stopping (sync)");

    protected override async Task OnUnmountAsync()
    {
        // Stand-in for a real "POST /stats/session-end". Demonstrates that the
        // framework awaits async unmount work on the IAsyncDisposable path.
        await Task.Delay(50);
        Emit("OnUnmountAsync: flushed (after 50ms)");
    }

    protected override Component? Render()
    {
        var current = _history.Count > 0 ? _history[^1].PriceUsd : 0m;
        var first = _history.Count > 0 ? _history[0].PriceUsd : 0m;
        var change = first == 0m ? 0m : (current - first) / first * 100m;
        var changeClass = change >= 0 ? "text-success" : "text-danger";
        var changeSign = change >= 0 ? "+" : string.Empty;

        return BsCard.Class(Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody[
                BsStack.Justify(BsJustify.Between).Align(BsAlign.Baseline).Class(Margin.Bottom(3))[
                    H3.Class("h4 mb-0 fw-semibold").Id("ticker-symbol")[Symbol],
                    Span.Class("text-secondary small")[
                        $"poll {IntervalMs} ms · {_history.Count}/{HistoryCapacity} pts"]
                ],
                BsStack.Gap(3).Align(BsAlign.Baseline).Class(Margin.Bottom(3))[
                    // One element, one class list, whichever state we're in — the price *text* changes,
                    // the box doesn't. Two spellings (fs-3 text-secondary → fs-2 fw-bold) meant the first
                    // tick resized and re-weighted the headline number 50 ms after mount, shoving the chart
                    // below it; and because the difference lived in a class attribute, it also made this
                    // demo's golden markup a race against the wall clock (#618).
                    Span.Class("fs-2 fw-bold").Id("ticker-price")[
                        _history.Count == 0
                            ? "Waiting for first tick…"
                            : $"${current.ToString("N2", CultureInfo.InvariantCulture)}"],
                    _history.Count > 1
                        ? Span.Class($"fs-6 fw-semibold {changeClass}").Id("ticker-change")[
                            $"{changeSign}{change.ToString("F2", CultureInfo.InvariantCulture)}% since first sample"]
                        : null
                ],
                _error is null
                    ? null
                    : BsAlert.Color(BsColor.Warning).Class("py-2 px-3 small mb-3").Id("ticker-error")[
                        BsIcon.Name(BsIconName.ExclamationTriangle).Class("me-2"), $"Feed error: {_error}"
                    ],
                // The chart is a server-rendered SVG drawn straight from the rolling buffer —
                // no canvas, no Chart.js, no JS. The fixed-height container gives the stretchy
                // <svg> a known box to fill.
                Div
                    .Class("ticker-chart-container")
                    .Id("ticker-chart")
                    .Style("position: relative; height: 160px;")[
                    // Always the <svg>. Sparkline already draws an empty labelled frame for an empty
                    // series, so the <p> placeholder this replaces was a second, worse answer to the same
                    // question — and swapping <p> for <svg> on the first tick was a tag-name change, which
                    // is the one thing the demo-markup golden cannot snapshot (#618).
                    Sparkline
                        .Values(_history.Select(p => (double)p.PriceUsd).ToList())
                        .Class("ticker-chart-svg")
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

            _error = null;
            StateHasChanged(); // the one render per tick
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            StateHasChanged(); // surface the #ticker-error alert
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

    private void Emit(string entry) => Log?.Invoke(entry);
}
