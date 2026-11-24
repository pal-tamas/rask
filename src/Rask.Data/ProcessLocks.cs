namespace Rask.Data;

/// <summary>
///     <see cref="Db.Lock(string, CancellationToken)" /> where the database has no lock across connections (SQLite): one holder per key in this
///     process, which is the one host a SQLite app runs on.
/// </summary>
internal static class ProcessLocks
{
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    /// <summary>
    ///     Takes each of <paramref name="names" /> — distinct, in ordinal order — in <paramref name="database" />, within one
    ///     wait; all or none.
    /// </summary>
    internal static async Task<IAsyncDisposable> Take(
        string database, IReadOnlyList<string> names, TimeSpan? wait, CancellationToken cancellationToken)
    {
        var deadline = NamedLock.Deadline.Of(wait);
        var held = new List<Held>(names.Count);
        try
        {
            foreach (var name in names)
            {
                held.Add(await TakeOne(database + name, name, deadline, cancellationToken).ConfigureAwait(false));
            }
        }
        catch
        {
            await Release(held).ConfigureAwait(false);
            throw;
        }

        return held.Count == 1 ? held[0] : new All(held);
    }

    private static async Task<Held> TakeOne(string key, string name, NamedLock.Deadline deadline, CancellationToken cancellationToken)
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
            taken = await entry.Turn.WaitAsync(deadline.Left ?? Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!taken)
            {
                Leave(key, entry);
            }
        }

        return taken ? new Held(key, entry) : throw NamedLock.TimedOut(name, deadline.Wait);
    }

    private static async ValueTask Release(List<Held> held)
    {
        for (var i = held.Count - 1; i >= 0; i--)
        {
            await held[i].DisposeAsync().ConfigureAwait(false);
        }
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

    /// <summary>Several names held at once, let go of the last taken first.</summary>
    private sealed class All(List<Held> held) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _released, 1) == 0 ? Release(held) : ValueTask.CompletedTask;
    }
}
