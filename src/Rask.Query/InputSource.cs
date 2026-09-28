namespace Rask.Querying;

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
    Func<TInput, CancellationToken, Task<TResult>> fetch) : IQuerySource<TResult>
{
    private TInput? _last;
    private bool _started;

    public bool TryAdvance(SessionQueryClient client, out QueryTarget target)
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
