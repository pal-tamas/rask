using Rask.ObjectStore;

namespace Rask.SQLite.Crdt.Sync.Tests;

/// <summary>
///     A bucket in a dictionary. Faithful about the two behaviours the engine actually depends on —
///     keys list in ordinal order, and <c>startAfter</c> is exclusive — because those are what make
///     forward-only reading correct, and a lenient fake would hide a real bug.
/// </summary>
internal sealed class FakeObjectStore : IObjectStore
{
    private readonly SortedDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    /// <summary>Set to simulate no connectivity: every call throws as a real client would see it.</summary>
    public bool Offline { get; set; }

    public int Gets { get; private set; }

    public int Puts { get; private set; }

    public IReadOnlyCollection<string> Keys => _objects.Keys;

    public Task<byte[]?> GetRangeAsync(
        string key, long offset, int count, CancellationToken cancellationToken = default)
    {
        ThrowIfOffline();
        if (!_objects.TryGetValue(key, out var bytes))
        {
            return Task.FromResult<byte[]?>(null);
        }

        var from = (int)Math.Min(offset, bytes.Length);
        return Task.FromResult<byte[]?>(bytes.AsSpan(from, Math.Min(count, bytes.Length - from)).ToArray());
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfOffline();
        Gets++;
        return Task.FromResult<Stream?>(
            _objects.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
    }

    public Task PutAsync(string key, byte[] content, CancellationToken cancellationToken = default)
    {
        ThrowIfOffline();
        Puts++;
        _objects[key] = content;
        return Task.CompletedTask;
    }

    public async Task PutAsync(
        string key, Stream content, long length, CancellationToken cancellationToken = default)
    {
        ThrowIfOffline();
        Puts++;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        _objects[key] = buffer.ToArray();
    }

    public Task<bool> TryCreateAsync(string key, byte[] content, CancellationToken cancellationToken = default)
    {
        ThrowIfOffline();
        if (_objects.ContainsKey(key))
        {
            return Task.FromResult(false);
        }

        _objects[key] = content;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<ObjectEntry>> ListAsync(
        string prefix, string? startAfter = null, CancellationToken cancellationToken = default)
    {
        ThrowIfOffline();

        var entries = _objects
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Where(kv => startAfter is null || string.CompareOrdinal(kv.Key, startAfter) > 0)
            .Select(kv => new ObjectEntry(kv.Key, kv.Value.Length, DateTimeOffset.UnixEpoch, null))
            .ToList();

        return Task.FromResult<IReadOnlyList<ObjectEntry>>(entries);
    }

    public Task<IReadOnlyList<string>> ListPrefixesAsync(
        string prefix, CancellationToken cancellationToken = default)
    {
        ThrowIfOffline();

        var prefixes = _objects.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k[prefix.Length..])
            .Select(rest => rest.IndexOf('/', StringComparison.Ordinal) is var slash && slash >= 0
                ? prefix + rest[..(slash + 1)]
                : null)
            .Where(p => p is not null)
            .Distinct(StringComparer.Ordinal)
            .Select(p => p!)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(prefixes);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfOffline();
        _objects.Remove(key);
        return Task.CompletedTask;
    }

    /// <summary>Removes an object without going through the engine — to simulate compaction mid-sync.</summary>
    public void RemoveDirectly(string key) => _objects.Remove(key);

    private void ThrowIfOffline()
    {
        if (Offline)
        {
            throw new HttpRequestException("no connectivity");
        }
    }
}
