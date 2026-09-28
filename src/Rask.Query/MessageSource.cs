using Rask.Cqrs;

namespace Rask.Querying;

/// <summary><c>() =&gt; new GetPerson(Id)</c>, where null means the input is not there yet.</summary>
internal sealed class MessageSource<TResult>(Func<IQuery<TResult>?> message) : IQuerySource<TResult>
{
    private IQuery<TResult>? _last;
    private bool _started;

    public bool TryAdvance(SessionQueryClient client, out QueryTarget target)
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
