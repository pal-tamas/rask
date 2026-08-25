namespace Rask.Query;

/// <summary>What a query holds.</summary>
/// <remarks>
///     Orthogonal to <see cref="FetchStatus" />, and deliberately so: a query can hold a result and
///     be fetching a newer one at the same time, which is the refresh-in-place case a single enum
///     cannot express. Rendering a spinner over data you already have is the bug that conflating
///     them produces.
/// </remarks>
public enum QueryStatus
{
    /// <summary>No result yet, and nothing has failed.</summary>
    Pending,

    /// <summary>
    ///     The last attempt threw. Any previously fetched result is still available on
    ///     <see cref="Query{TResult}.Data" /> — a failed refresh does not blank a working page.
    /// </summary>
    Error,

    /// <summary>A result is available and the last attempt succeeded.</summary>
    Success,
}

/// <summary>Whether a request is on the wire.</summary>
public enum FetchStatus
{
    /// <summary>Nothing in flight.</summary>
    Idle,

    /// <summary>A request is in flight, whether it is the first or a refresh.</summary>
    Fetching,

    /// <summary>
    ///     Would fetch, but must not. Today that means <see cref="QueryOptions.Enabled" /> is false —
    ///     a query waiting on something the user has not chosen yet. Offline will land here too.
    /// </summary>
    Paused,
}

/// <summary>Where a mutation is in its one-shot lifecycle.</summary>
public enum MutationStatus
{
    /// <summary>Never run, or reset.</summary>
    Idle,

    /// <summary>Dispatched and not yet answered. This is what disables the button.</summary>
    Pending,

    /// <summary>The last run threw.</summary>
    Error,

    /// <summary>The last run succeeded.</summary>
    Success,
}
