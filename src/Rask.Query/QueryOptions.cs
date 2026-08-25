namespace Rask.Query;

/// <summary>
///     How long a query's result may be served, and how long it is kept after nothing is rendering it.
/// </summary>
/// <remarks>
///     The defaults are TanStack Query's, deliberately: the behaviour is the one a large number of
///     people already have in their heads, and matching it means their knowledge transfers rather
///     than having to be unlearned for the C# half of the same app.
/// </remarks>
public sealed record QueryOptions
{
    /// <summary>The shared default, used by every call that names none.</summary>
    public static QueryOptions Default { get; } = new();

    /// <summary>
    ///     How long after a fetch the result is considered fresh. While fresh it is served with no
    ///     request at all; once stale it is still served immediately, and a refetch runs behind it.
    /// </summary>
    /// <remarks>
    ///     Zero by default — TanStack's default — so a query refetches whenever a component starts
    ///     observing it. Raise it for data that does not move: a currency table, a product catalogue.
    /// </remarks>
    public TimeSpan StaleTime { get; init; } = TimeSpan.Zero;

    /// <summary>
    ///     How long an entry nothing renders any more is kept before it is dropped. Navigating away
    ///     and back inside this window paints instantly instead of showing a spinner.
    /// </summary>
    public TimeSpan GcTime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Whether the query may run at all. False holds it at <see cref="QueryStatus.Pending" /> with
    ///     <see cref="FetchStatus.Paused" /> and does not fetch — for a query that depends on
    ///     something the user has not chosen yet.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    ///     How many further attempts a failed fetch gets. TanStack's default of three.
    /// </summary>
    /// <remarks>
    ///     Safe to default on because it applies to <b>queries only</b>, and a Rask query is defined
    ///     as safe and idempotent — the transport enforces that by refusing to send a command as a
    ///     GET. Mutations get no retry at all, for the same reason TanStack gives them none: running
    ///     a command twice is not a free action.
    /// </remarks>
    public int Retry { get; init; } = 3;

    /// <summary>
    ///     How long to wait before attempt <c>n</c> (zero-based). Defaults to TanStack's exponential
    ///     backoff: one second doubling, capped at thirty.
    /// </summary>
    public Func<int, TimeSpan>? RetryDelay { get; init; }

    /// <summary>
    ///     Whether a given failure is worth retrying at all. Defaults to
    ///     <see cref="QueryOptions.IsWorthRetrying" />.
    /// </summary>
    public Func<Exception, bool>? ShouldRetry { get; init; }

    /// <summary>
    ///     Refetches on this interval while something is rendering the query. Null — the default —
    ///     never polls.
    /// </summary>
    /// <remarks>
    ///     A polling query keeps a session doing work, so it stops when the query is disposed, and
    ///     also once every component that was reading it has gone. Dispose the query from
    ///     <c>OnUnmount</c>: the second check is a safety net, not the mechanism.
    /// </remarks>
    public TimeSpan? RefetchInterval { get; init; }

    /// <summary>The default backoff: one second doubling, capped at thirty.</summary>
    public static TimeSpan DefaultRetryDelay(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(1000d * Math.Pow(2, attempt), 30_000d));

    /// <summary>
    ///     The default retry rule: everything except a refusal and a cancellation.
    /// </summary>
    /// <remarks>
    ///     A 4xx will never succeed on a retry — a 403 is not a network blip — so retrying one turns
    ///     a single refused request into four and delays telling the user anything by several
    ///     seconds. A cancellation is not a failure at all: it means nothing is rendering the query
    ///     any more.
    /// </remarks>
    public static bool IsWorthRetrying(Exception error) => error switch
    {
        OperationCanceledException => false,
        Rask.Cqrs.RemoteDispatchException { StatusCode: >= 400 and < 500 } => false,
        _ => true,
    };
}
