using Rask.Cqrs;

namespace Rask.Query;

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

/// <summary>
///     Where a lambda query gets its target: re-run at every read, and asked only whether the answer
///     changed.
/// </summary>
/// <typeparam name="TResult">What the query returns.</typeparam>
internal abstract class QuerySource<TResult>
{
    /// <summary>
    ///     Runs the lambda; true with a new <paramref name="target" /> when its answer differs from the
    ///     last one, false — without building anything — when it does not.
    /// </summary>
    public abstract bool TryAdvance(SessionQueryClient client, out QueryTarget target);
}

/// <summary><c>() =&gt; new GetPerson(Id)</c>, where null means the input is not there yet.</summary>
internal sealed class MessageSource<TResult>(Func<IQuery<TResult>?> message) : QuerySource<TResult>
{
    private IQuery<TResult>? _last;
    private bool _started;

    public override bool TryAdvance(SessionQueryClient client, out QueryTarget target)
    {
        var next = message();

        // Records compare structurally, so an unchanged message is Equals — the same test the cache key
        // itself makes.
        if (_started && Equals(next, _last))
        {
            target = default;
            return false;
        }

        _started = true;
        _last = next;
        target = next is null ? QueryTarget.Paused : QueryTarget.ForMessage(client, next);
        return true;
    }
}

/// <summary>
///     <c>() =&gt; Id</c> under a key prefix, fetched by a function handed that input.
/// </summary>
/// <remarks>
///     The input is compared with <see cref="EqualityComparer{T}.Default" /> before any key is built,
///     so a read whose input has not changed — nearly every one — allocates nothing for a value-type
///     input, a tuple of them included.
/// </remarks>
internal sealed class InputSource<TInput, TResult>(
    QueryKey prefix,
    Func<TInput> input,
    Func<TInput, CancellationToken, Task<TResult>> fetch) : QuerySource<TResult>
{
    private TInput? _last;
    private bool _started;

    public override bool TryAdvance(SessionQueryClient client, out QueryTarget target)
    {
        var next = input();
        if (_started && EqualityComparer<TInput>.Default.Equals(next, _last))
        {
            target = default;
            return false;
        }

        _started = true;
        _last = next;
        target = QueryTarget.ForInput(prefix, next, fetch);
        return true;
    }
}
