using System.Collections.Concurrent;

namespace Rask.Core.Caching;

/// <summary>
///     The in-process <see cref="IFragmentStore" />: a bounded dictionary of markup with absolute and sliding expiry.
/// </summary>
/// <remarks>
///     <para>
///         Not <c>MemoryCache</c>, deliberately: <c>Rask.Core</c> ships to the browser, and taking
///         <c>Microsoft.Extensions.Caching.Memory</c> for one map would put a package in every WebAssembly app's
///         download. What a fragment needs is small — expiry, a size bound, tags — and is all here.
///     </para>
///     <para>
///         The size is counted in UTF-16 bytes of markup. Past <see cref="CachedOptions.SizeLimit" /> the least
///         recently read entries go first, down to nine tenths of the limit, so a full store is not compacted on
///         every write. An entry larger than <see cref="CachedOptions.MaxEntrySize" /> is never stored: it is drawn
///         live every time rather than pushing everything else out.
///     </para>
/// </remarks>
internal sealed class MemoryFragmentStore : IFragmentStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _compaction = new();
    private readonly TimeProvider _time;
    private long _size;

    internal MemoryFragmentStore(CachedOptions? options = null, TimeProvider? time = null)
    {
        Options = options ?? new CachedOptions();
        _time = time ?? Clock.TimeProvider;
    }

    /// <inheritdoc />
    public CachedOptions Options { get; }

    /// <summary>The store every render shares when the host registered none — the browser's, and a bare test's.</summary>
    internal static MemoryFragmentStore Shared { get; } = new();

    /// <summary>The bytes of markup held now.</summary>
    internal long Size => Interlocked.Read(ref _size);

    /// <summary>How many entries are held now, expired ones not yet swept included.</summary>
    internal int Count => _entries.Count;

    public ValueTask<string?> Get(string key, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var now = _time.GetUtcNow();
        if (entry.ExpiredAt(now))
        {
            Remove(key, entry);
            return ValueTask.FromResult<string?>(null);
        }

        entry.Touch(now);
        return ValueTask.FromResult<string?>(entry.Html);
    }

    public ValueTask Set(string key, string html, FragmentLifetime lifetime, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        var size = SizeOf(html);
        if (size > Options.MaxEntrySize || size > Options.SizeLimit)
        {
            return ValueTask.CompletedTask;
        }

        var now = _time.GetUtcNow();
        var entry = new Entry(html, size, [.. tags], now + lifetime.Absolute, lifetime.Sliding, now);
        Put(key, entry);

        if (Size > Options.SizeLimit)
        {
            Compact(now);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask Forget(string tag, CancellationToken cancellationToken = default)
    {
        foreach (var (key, entry) in _entries)
        {
            if (Array.IndexOf(entry.Tags, tag) >= 0)
            {
                Remove(key, entry);
            }
        }

        return ValueTask.CompletedTask;
    }

    // Counted once per entry that actually lands: an update factory may run more than once under contention.
    private void Put(string key, Entry entry)
    {
        while (true)
        {
            if (_entries.TryGetValue(key, out var replaced))
            {
                if (_entries.TryUpdate(key, entry, replaced))
                {
                    Interlocked.Add(ref _size, entry.Size - replaced.Size);
                    return;
                }
            }
            else if (_entries.TryAdd(key, entry))
            {
                Interlocked.Add(ref _size, entry.Size);
                return;
            }
        }
    }

    // UTF-16, as the string is held.
    private static long SizeOf(string html) => (long)html.Length * sizeof(char);

    private void Remove(string key, Entry entry)
    {
        // Only the entry that was read: a newer one stored under the key since stays.
        if (_entries.TryRemove(new KeyValuePair<string, Entry>(key, entry)))
        {
            Interlocked.Add(ref _size, -entry.Size);
        }
    }

    // Expired entries first, then the least recently read, until the store is back under nine tenths of its limit.
    private void Compact(DateTimeOffset now)
    {
        lock (_compaction)
        {
            var target = Options.SizeLimit / 10 * 9;
            foreach (var (key, entry) in _entries)
            {
                if (entry.ExpiredAt(now))
                {
                    Remove(key, entry);
                }
            }

            if (Size <= target)
            {
                return;
            }

            foreach (var (key, entry) in _entries.OrderBy(static e => e.Value.LastReadTicks))
            {
                Remove(key, entry);
                if (Size <= target)
                {
                    return;
                }
            }
        }
    }

    private sealed class Entry(string html, long size, string[] tags, DateTimeOffset absoluteEnd, TimeSpan? sliding, DateTimeOffset storedAt)
    {
        private long _lastReadTicks = storedAt.UtcTicks;

        internal string Html { get; } = html;

        internal long Size { get; } = size;

        internal string[] Tags { get; } = tags;

        internal long LastReadTicks => Interlocked.Read(ref _lastReadTicks);

        internal bool ExpiredAt(DateTimeOffset now) =>
            now >= absoluteEnd || (sliding is { } window && now.UtcTicks - LastReadTicks >= window.Ticks);

        internal void Touch(DateTimeOffset now) => Interlocked.Exchange(ref _lastReadTicks, now.UtcTicks);
    }
}
