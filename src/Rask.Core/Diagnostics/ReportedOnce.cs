namespace Rask.Core.Diagnostics;

/// <summary>
///     The keys <see cref="RaskDiagnostics.ReportOnce" /> has delivered: the last <see cref="Cap" /> of them, the oldest
///     forgotten to make room, so a buggy app churning distinct keys costs at most that many and may hear an old one
///     again — where a set that stopped taking keys at the cap silenced every ReportOnce in the process for good.
/// </summary>
internal sealed class ReportedOnce
{
    internal const int Cap = 1024;

    private readonly Lock _gate = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    /// <summary>Records <paramref name="key" />; false when it was already recorded.</summary>
    internal bool Add(string key)
    {
        lock (_gate)
        {
            if (!_keys.Add(key))
            {
                return false;
            }

            _order.Enqueue(key);
            if (_order.Count > Cap)
            {
                _keys.Remove(_order.Dequeue());
            }

            return true;
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _keys.Clear();
            _order.Clear();
        }
    }
}
