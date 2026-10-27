namespace Rask.Server.Diagnostics;

/// <summary>
///     The last few sessions this host removed, and why, so a tab that comes back to one can be told what happened to it.
/// </summary>
/// <remarks>
///     A refused attach is answered "no session has that id" whether the id was never issued or was removed a moment
///     ago, and only the second is a reload a reader noticed. Bounded: the oldest entry makes room for the newest.
/// </remarks>
internal sealed class RecentRemovals
{
    internal const int Capacity = 256;

    private readonly Entry[] _entries = new Entry[Capacity];
    private readonly Lock _gate = new();
    private int _next;

    public void Add(string sessionId, SessionRemoval why, TimeSpan limit, long at)
    {
        lock (_gate)
        {
            _entries[_next] = new Entry(sessionId, why, limit, at);
            _next = (_next + 1) % Capacity;
        }
    }

    /// <summary>The removal of <paramref name="sessionId" />, newest first, if it is still remembered.</summary>
    public Entry? Find(string sessionId)
    {
        lock (_gate)
        {
            for (var i = 1; i <= Capacity; i++)
            {
                var entry = _entries[(_next - i + Capacity) % Capacity];
                if (entry is null)
                {
                    return null;
                }

                if (string.Equals(entry.SessionId, sessionId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }
    }

    internal sealed record Entry(string SessionId, SessionRemoval Why, TimeSpan Limit, long At);
}
