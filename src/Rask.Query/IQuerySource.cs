namespace Rask.Querying;

/// <summary>
///     Where a lambda query gets its target: re-run at every read, and asked only whether the answer
///     changed.
/// </summary>
/// <typeparam name="TResult">What the query returns.</typeparam>
#pragma warning disable S2326 // phantom: ties a source to the Query<TResult> it feeds
internal interface IQuerySource<TResult>
#pragma warning restore S2326
{
    /// <summary>
    ///     Runs the lambda; true with a new <paramref name="target" /> when its answer differs from the
    ///     last one, false — without building anything — when it does not.
    /// </summary>
    bool TryAdvance(SessionQueryClient client, out QueryTarget target);
}
