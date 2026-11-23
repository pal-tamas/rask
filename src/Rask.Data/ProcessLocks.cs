namespace Rask.Data;

/// <summary>
///     <see cref="Db.Lock" /> where the database has no lock across connections (SQLite): one holder per key in this
///     process, which is the one host a SQLite app runs on.
/// </summary>
internal static class ProcessLocks
{
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    internal static async Task<IAsyncDisposable> Take(string key, string name, TimeSpan? wait, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                Entries.Add(key, entry);
            }

            entry.Users++;
        }

        var taken = false;
        try
        {
            taken = await entry.Turn.WaitAsync(wait ?? Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!taken)
            {
                Leave(key, entry);
            }
        }

        return taken ? new Held(key, entry) : throw NamedLock.TimedOut(name, wait);
    }

    private static void Leave(string key, Entry entry)
    {
        lock (Gate)
        {
            // Forgotten once nobody holds or waits for it, so a name used once does not stay for the process's life.
            if (--entry.Users == 0)
            {
                Entries.Remove(key);
            }
        }
    }

    private sealed class Entry
    {
        // Never disposed: a SemaphoreSlim without an AvailableWaitHandle holds nothing that needs it.
        internal SemaphoreSlim Turn { get; } = new(1, 1);

        internal int Users { get; set; }
    }

    private sealed class Held(string key, Entry entry) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                entry.Turn.Release();
                Leave(key, entry);
            }

            return ValueTask.CompletedTask;
        }
    }
}
