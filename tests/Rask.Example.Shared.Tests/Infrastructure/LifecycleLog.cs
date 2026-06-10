namespace Rask.Example.Shared.Tests.Infrastructure;

// Thread-safe append-only log for lifecycle-probe tests. The async lifecycle dispatcher
// re-renders on every await, so multiple threads may push entries during a single
// RenderAsLiveRoot call. Snapshot() returns a defensive copy for assertions.
internal sealed class LifecycleLog
{
    private readonly List<string> _entries = [];

    public int Count
    {
        get
        {
            lock (_entries)
            {
                return _entries.Count;
            }
        }
    }

    public void Add(string entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_entries)
        {
            return _entries.ToArray();
        }
    }

    public bool Contains(string fragment, StringComparison cmp = StringComparison.Ordinal)
    {
        lock (_entries)
        {
            foreach (var e in _entries)
            {
                if (e.Contains(fragment, cmp))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
