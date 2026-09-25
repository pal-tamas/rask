using Rask.Cqrs;

namespace Rask.Site.Features;

// A tiny vertical slice that shows all four Rask.Cqrs message shapes wired reflection-free by the
// source generator: a query, a command that returns a value, a notification the command publishes,
// and a pipeline behavior (decorator) that wraps every dispatch. The store is the slice's state.
public sealed class CqrsCounterStore
{
    private readonly Queue<string> _log = new();

    public int Count { get; private set; }

    public IReadOnlyList<string> Log => _log.ToArray();

    public int IncrementBy(int by)
    {
        Count += by;
        return Count;
    }

    public void Note(string entry)
    {
        _log.Enqueue(entry);
        while (_log.Count > 6)
        {
            _log.Dequeue();
        }
    }
}
