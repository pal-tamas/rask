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
        if (Observers == 0)
        {
            AbandonedAt = now;
        }
    }

    public bool IsCollectable(TimeSpan gcTime, DateTimeOffset now) =>
        Observers == 0 && AbandonedAt is { } at && now - at >= gcTime;

    public void BeginFetch(Task fetch) => InFlight = fetch;

    public void Succeeded(object? data, DateTimeOffset now)
    {
        Data = data;
        HasData = true;
        Error = null;
        FetchedAt = now;
        InFlight = null;
        _fetchOwed = false;
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
        Notify();
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
