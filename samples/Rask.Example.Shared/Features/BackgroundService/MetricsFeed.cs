namespace Rask.Example.Shared.Features;

// An app-wide background process that pushes updates to the UI — the counterpart to
// LiveTicker. Where LiveTicker runs a poll loop *inside* a single component (the loop
// is born and dies with that component's lifecycle), MetricsFeed is a DI **singleton**
// that runs its own loop independent of any component. One producer, many subscribers:
// components opt in with `feed.Updated += StateHasChanged` in OnMount and opt out in
// OnUnmount — the same shape the layout uses for RouteState.Changed (ShowcaseLayout.cs),
// but driven by a background timer instead of navigation.
//
// Lifetime & sharing — registered AddSingleton (ExampleServiceCollectionExtensions.cs):
//   * One instance per application. The loop keeps ticking across navigations and, on
//     the Server transport, across every connected session — it is decoupled from the
//     component tree, so the feed advances even while nobody is rendering it. (Contrast
//     DemoUserProvider, which is deliberately *scoped*: a per-user principal must NOT be
//     shared. A public synthetic metric stream is fine to share; auth state is not.)
//   * Created lazily on first resolution (the first component that injects IMetricsFeed),
//     then runs until the host disposes it on shutdown via IAsyncDisposable.
//
// Thread-safety — the load-bearing part:
//   * Updated fires from a background thread (the loop continuation). Each subscriber's
//     StateHasChanged() schedules a render under *its own* session render lock and is a
//     no-op once the component has unmounted, so a tick racing an unsubscribe is benign
//     (see Component.StateHasChanged).
//   * State is published as a single **immutable** MetricsSnapshot swapped by reference.
//     A reader on the render thread always sees a consistent (Current, Recent) pair from
//     one atomic read — there is no torn read while the loop builds the next snapshot.
//     This is the race LiveTicker can ignore only because its loop is component-local and
//     strictly serialized; an app-wide singleton genuinely crosses threads, so the
//     copy-on-write snapshot is what makes it correct.
//
// The feed is fully synthetic (a bounded random walk), so the demo is deterministic in
// shape and offline-safe — swapping in a real source (a metrics endpoint, a message bus)
// is a one-method change to Step, and the subscribe/snapshot/dispose story is unchanged.
public interface IMetricsFeed
{
    // The latest consistent snapshot. Safe to read from any thread.
    MetricsSnapshot State { get; }

    // Raised after each tick, once State has been swapped to the new snapshot.
    event Action? Updated;
}

public sealed class MetricsFeed : IMetricsFeed, IAsyncDisposable
{
    // ~1 min of history at the default cadence; bounded so a long-lived app doesn't grow
    // the rolling buffer without limit (same discipline as LiveTicker.HistoryCapacity).
    private const int HistoryCapacity = 60;
    private const int IntervalMs = 1000;

    private readonly CancellationTokenSource _cts = new();
    private readonly int _intervalMs;
    private readonly Task _loop;

    private MetricsSnapshot _state;

    public MetricsFeed() : this(IntervalMs)
    {
    }

    // Internal ctor lets the unit tests run the loop fast without waiting out the
    // production 1 s cadence.
    internal MetricsFeed(int intervalMs)
    {
        _intervalMs = intervalMs;
        _state = CreateInitialSnapshot(DateTimeOffset.UtcNow);
        _loop = RunAsync(_cts.Token);
    }

    public MetricsSnapshot State => Volatile.Read(ref _state);

    public event Action? Updated;

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the loop's in-flight Task.Delay is cancelled.
        }

        _cts.Dispose();
    }

    // First snapshot so the gauge shows numbers and the chart has a point before the
    // first tick lands.
    public static MetricsSnapshot CreateInitialSnapshot(DateTimeOffset now)
    {
        var sample = new MetricsSample(Tick: 0, CpuPercent: 50, ActiveJobs: 4, At: now);
        return new MetricsSnapshot(sample, new[] { sample });
    }

    // Pure transition: previous snapshot + the tick's timestamp ⇒ next snapshot. Kept
    // free of the loop/timer so it unit-tests deterministically. The randomness is the
    // only non-deterministic part and only affects the *values*, not the invariants the
    // tests assert (Tick increments, Recent stays capped, Current is the last point).
    internal static MetricsSnapshot Step(MetricsSnapshot prev, DateTimeOffset now)
    {
        var c = prev.Current;
        var cpu = Math.Clamp(c.CpuPercent + ((Random.Shared.NextDouble() - 0.5) * 12), 2, 99);
        var jobs = Math.Clamp(c.ActiveJobs + Random.Shared.Next(-2, 3), 0, 24);
        var next = new MetricsSample(c.Tick + 1, Math.Round(cpu, 1), jobs, now);
        return new MetricsSnapshot(next, AppendCapped(prev.Recent, next, HistoryCapacity));
    }

    // Copy-on-write append: returns a fresh array (never mutates the previous one), so the
    // snapshot a reader already holds is immutable and the swap below is a clean handover.
    private static IReadOnlyList<MetricsSample> AppendCapped(
        IReadOnlyList<MetricsSample> prev, MetricsSample next, int cap)
    {
        var start = prev.Count >= cap ? prev.Count - cap + 1 : 0;
        var len = prev.Count - start + 1;
        var arr = new MetricsSample[len];
        for (var i = 0; i < len - 1; i++)
        {
            arr[i] = prev[start + i];
        }

        arr[len - 1] = next;
        return arr;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_intervalMs, ct).ConfigureAwait(false);

                // Build the next snapshot, then publish it as one atomic reference swap
                // BEFORE notifying — so every subscriber that wakes reads the new state.
                Volatile.Write(ref _state, Step(Volatile.Read(ref _state), DateTimeOffset.UtcNow));
                Updated?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose — the loop simply exits.
        }
    }
}

// One tick of the feed.
public readonly record struct MetricsSample(int Tick, double CpuPercent, int ActiveJobs, DateTimeOffset At);

// An immutable view of the feed: the latest sample plus the rolling history. Swapped as a
// single reference each tick so readers never observe a half-updated state.
public sealed record MetricsSnapshot(MetricsSample Current, IReadOnlyList<MetricsSample> Recent);
