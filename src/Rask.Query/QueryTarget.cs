using Rask.Cqrs;

namespace Rask.Querying;

/// <summary>What a query shows: the entry's key, how to fill it, and whether it may fetch yet.</summary>
internal readonly record struct QueryTarget(QueryKey Key, Func<CancellationToken, Task<object?>> Fetch, bool IsPaused)
{
    /// <summary>
    ///     The entry every query waiting on an input watches. It is never fetched — a paused query
    ///     cannot start one — so sharing it across every waiting query costs one empty entry.
    /// </summary>
    public static QueryTarget Paused { get; } =
        new(QueryKey.Of(typeof(QueryTarget), "paused"), static _ => Task.FromResult<object?>(null), IsPaused: true);

    public static QueryTarget ForMessage<TResult>(SessionQueryClient client, IQuery<TResult> message)
    {
        // A lambda query's message does not exist until the lambda has run, so [Live] is read here rather than
        // where the query was declared — otherwise a dependent query would never be live.
        client.WatchDeclared(message);
        return new(MessageKey.For(message), client.DispatchFetch(message), IsPaused: false);
    }

    /// <summary>
    ///     <c>[..prefix, input]</c>, fetched with the same <paramref name="input" /> the key was built from
    ///     — so a fetch still running when the input changes caches under its own key, never the new one.
    /// </summary>
    public static QueryTarget ForInput<TInput, TResult>(
        QueryKey prefix,
        TInput input,
        Func<TInput, CancellationToken, Task<TResult>> fetch) =>
        input is null
            ? Paused
            : new(QueryKey.Of([.. prefix.Parts, input]), async ct => await fetch(input, ct).ConfigureAwait(false), false);
}
