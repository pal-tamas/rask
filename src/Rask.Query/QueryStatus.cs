namespace Rask.Querying;

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
