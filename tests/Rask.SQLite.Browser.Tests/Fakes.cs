using Rask.Core.Browser;
using Rask.SQLite.Snapshots;

namespace Rask.SQLite.Browser.Tests;

/// <summary>An in-memory <see cref="IIndexedDb" />, standing in for the browser's real one.</summary>
internal sealed class FakeIndexedDb : IIndexedDb
{
    private readonly Dictionary<string, FakeKeyValueStore> _stores = [];

    public bool Supported { get; set; } = true;

    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(Supported);

    public ValueTask<IKeyValueStore> OpenStoreAsync(string name)
    {
        if (!_stores.TryGetValue(name, out var store))
        {
            store = new FakeKeyValueStore();
            _stores[name] = store;
        }

        return ValueTask.FromResult<IKeyValueStore>(store);
    }

    public FakeKeyValueStore Store(string name) => (FakeKeyValueStore)OpenStoreAsync(name).AsTask().Result;
}

internal sealed class FakeKeyValueStore : IKeyValueStore
{
    // Bytes, not strings: the real store keeps a Uint8Array, and a fake that round-tripped through text
    // would hide an encoding bug rather than catch one.
    public Dictionary<string, byte[]> Values { get; } = [];

    public ValueTask SetAsync(string key, string value) => throw new NotSupportedException("Use SetBytesAsync.");

    public ValueTask<string?> GetAsync(string key) => throw new NotSupportedException("Use GetBytesAsync.");

    public ValueTask SetBytesAsync(string key, byte[] value)
    {
        Values[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetBytesAsync(string key) =>
        ValueTask.FromResult(Values.TryGetValue(key, out var value) ? value : null);

    public ValueTask DeleteAsync(string key)
    {
        Values.Remove(key);
        return ValueTask.CompletedTask;
    }

    // Deliberately unsorted, so nothing under test can lean on insertion order the browser does not promise.
    public ValueTask<string[]> KeysAsync() => ValueTask.FromResult(Values.Keys.OrderBy(k => k.Length).ToArray());

    public ValueTask ClearAsync()
    {
        Values.Clear();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
///     An <see cref="IWebLocks" /> that models the one behaviour under test: a lock is held for as long
///     as its callback runs, and a second request for a held lock fails immediately.
/// </summary>
internal sealed class FakeWebLocks : IWebLocks
{
    private readonly HashSet<string> _held = [];

    public bool Supported { get; set; } = true;

    /// <summary>Pre-hold a lock, standing in for another tab that already owns it.</summary>
    public void HoldElsewhere(string name) => _held.Add(name);

    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(Supported);

    public async ValueTask RequestAsync(string name, Func<Task> work, LockMode mode = LockMode.Exclusive)
    {
        _held.Add(name);
        try
        {
            await work();
        }
        finally
        {
            _held.Remove(name);
        }
    }

    public async ValueTask<bool> TryRequestAsync(string name, Func<Task> work, LockMode mode = LockMode.Exclusive)
    {
        if (!_held.Add(name))
        {
            return false;
        }

        try
        {
            await work();
        }
        finally
        {
            _held.Remove(name);
        }

        return true;
    }

    public ValueTask<IReadOnlyList<LockInfo>> QueryAsync() =>
        ValueTask.FromResult<IReadOnlyList<LockInfo>>(
            [.. _held.Select(n => new LockInfo(n, "exclusive", null, true))]);
}

internal sealed class RecordingSnapshotter : ISqliteSnapshotter
{
    public int Count { get; private set; }

    public Exception? Throws { get; set; }

    public Task<string> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        Count++;
        return Throws is not null ? Task.FromException<string>(Throws) : Task.FromResult($"snapshot-{Count}");
    }
}
