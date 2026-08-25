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
    ///     Whether the query may run at all. False holds it at <see cref="Query{TResult}.IsLoading" />
    ///     without fetching — for a query that depends on something the user has not chosen yet.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
