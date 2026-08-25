using System.Diagnostics.CodeAnalysis;

namespace Rask.Query;

/// <summary>One cached result, its freshness, and whatever fetch is currently in flight for it.</summary>
internal sealed class QueryEntry
{
    private readonly List<Action> _listeners = [];

    /// <summary>
    ///     Whether a fetch is owed. True until an attempt finishes, however it finishes.
    /// </summary>
    /// <remarks>
    ///     Tracked separately from <see cref="FetchedAt" />, and that separation is load-bearing. A
    ///     failed attempt leaves FetchedAt null, so deriving "needs a fetch" from it means a failure
    ///     notifies, the notification sees a fetch is owed, it retries, it fails again — a hot retry
    ///     loop against a server that is already unwell, until the stack runs out. Clearing this on
    ///     failure too makes a retry something the caller asks for.
    /// </remarks>
    private bool _fetchOwed = true;

    /// <summary>Cancels the fetch currently in flight, when there is one.</summary>
    private CancellationTokenSource? _fetchCts;

    /// <summary>
    ///     How long this entry survives with no observers.
    /// </summary>
    /// <remarks>
    ///     Held here rather than read from the options at collection time, because collection walks
    ///     entries and not queries — reading a single default there quietly ignored any query that
    ///     asked for a longer lifetime. Two queries sharing a key keep the longest of what they ask
    ///     for: the entry has to outlive whichever of them needs it most.
    /// </remarks>
    public TimeSpan GcTime { get; private set; } = QueryOptions.Default.GcTime;

    public void RequireGcTime(TimeSpan gcTime)
    {
        if (gcTime > GcTime)
        {
            GcTime = gcTime;
        }
    }

    public object? Data { get; private set; }

    public Exception? Error { get; private set; }

    public bool HasData { get; private set; }

    public DateTimeOffset? FetchedAt { get; private set; }

    /// <summary>
    ///     The fetch currently running, shared by every caller that asks while it is in flight.
    /// </summary>
    /// <remarks>
    ///     This is the deduplication: three components rendering the same query in one frame produce
    ///     one request, not three. Without it the cache would still return the same value eventually
    ///     while tripling the load that produced it.
    /// </remarks>
    public Task? InFlight { get; private set; }

    /// <summary>How many live <see cref="Query{TResult}" /> handles are watching this entry.</summary>
    public int Observers { get; private set; }

    /// <summary>When the last observer went away, which starts the GC clock.</summary>
    public DateTimeOffset? AbandonedAt { get; private set; }

    /// <summary>Marks the entry stale without dropping it, so it is refetched but still served meanwhile.</summary>
    public void Invalidate()
    {
        FetchedAt = null;
        _fetchOwed = true;
    }

    /// <summary>Tells every observer to re-read, without changing what is held.</summary>
    public void NotifyChanged() => Notify();

    /// <summary>
    ///     The entry holds no fetch result: never loaded, or invalidated since.
    /// </summary>
    /// <remarks>
    ///     This, and not <see cref="IsStale" />, is what a change notification acts on. With the
    ///     default StaleTime of zero an entry is stale the instant it lands, so refetching on
    ///     staleness from inside a notification recurses until the stack runs out — succeed, notify,
    ///     observe stale, refetch, succeed. Invalidation is an event; staleness is a standing
    ///     condition, and only the event is a trigger.
    /// </remarks>
    public bool NeedsRefetch => _fetchOwed;

    public bool IsStale(TimeSpan staleTime, DateTimeOffset now) =>
        FetchedAt is not { } at || now - at >= staleTime;

    public void Observe(Action listener)
    {
        _listeners.Add(listener);
        Observers++;
        AbandonedAt = null;
    }

    public void Unobserve(Action listener, DateTimeOffset now)
    {
        if (!_listeners.Remove(listener))
        {
            return;
        }

        Observers--;
        if (Observers != 0)
        {
            return;
        }

        AbandonedAt = now;

        // Nothing is rendering this any more, so whatever is in flight is work for a screen that has
        // gone. Cancelling it is the difference between a component unmount releasing a request and
        // a navigation leaving one running to completion against a database.
        CancelInFlight();
    }

    public bool IsCollectable(DateTimeOffset now) =>
        Observers == 0 && AbandonedAt is { } at && now - at >= GcTime;

    public void BeginFetch(Task fetch, CancellationTokenSource cancellation)
    {
        InFlight = fetch;
        _fetchCts = cancellation;
    }

    public void Succeeded(object? data, DateTimeOffset now)
    {
        Data = data;
        HasData = true;
        Error = null;
        FetchedAt = now;
        InFlight = null;
        _fetchOwed = false;
        ReleaseCancellation();
        Notify();
    }

    public void Failed(Exception error)
    {
        Error = error;
        // Deliberately keeps Data and HasData. A refetch that fails should leave what is on screen
        // there with an error beside it, rather than blanking a working page because the network
        // blinked — which is what dropping the data would do.
        InFlight = null;
        _fetchOwed = false;
        ReleaseCancellation();
        Notify();
    }

    /// <summary>
    ///     The fetch was cancelled rather than finishing. Clears the in-flight state but leaves the
    ///     fetch owed, because nothing was actually retrieved: the next observer should try again.
    /// </summary>
    public void Abandoned()
    {
        InFlight = null;
        ReleaseCancellation();
    }

    /// <summary>Cancels and releases the in-flight fetch's cancellation source.</summary>
    private void CancelInFlight()
    {
        var cancellation = _fetchCts;
        _fetchCts = null;
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The fetch finished between the null check and here, and disposed it. Nothing to cancel.
        }

        cancellation.Dispose();
    }

    /// <summary>Releases the source of a fetch that has finished on its own.</summary>
    private void ReleaseCancellation()
    {
        var cancellation = _fetchCts;
        _fetchCts = null;
        cancellation?.Dispose();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A listener is a component re-render. One that throws must not stop the others "
                        + "from being told, and must not surface as a failure of the fetch that succeeded.")]
    private void Notify()
    {
        // Copied before iterating: a listener may unobserve while being notified — a component that
        // unmounts in response to the data arriving — and that would otherwise mutate the list mid-walk.
        foreach (var listener in _listeners.ToArray())
        {
            try
            {
                listener();
            }
            catch
            {
                // See the justification above.
            }
        }
    }
}
