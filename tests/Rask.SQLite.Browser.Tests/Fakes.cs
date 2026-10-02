using System.Collections.Concurrent;
using Microsoft.JSInterop;
using Rask.Core.Browser;
using Rask.SQLite.Snapshots;
using Rask.Web;
using Lock = Rask.Web.Types.Lock;
using LockMode = Rask.Web.Types.LockMode;
using LockOptions = Rask.Web.Types.LockOptions;
using StorageManager = Rask.Web.Types.StorageManager;

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
///     The browser's <c>navigator.locks</c> and <c>navigator.storage</c>, faked with Rask.Web's own fakes. The locks
///     model the one behaviour under test: a lock is held for as long as its handler runs, and an <c>ifAvailable</c>
///     request for a held one is handed no lock.
/// </summary>
/// <remarks>
///     A model, entered with <see cref="Enter" /> from the test's own flow — a web fake lasts for the flow it was made
///     in, and the host's lock and availability tasks inherit it from there.
/// </remarks>
internal sealed class FakeWebApis
{
    // The availability watcher polls from its own task, so the model is read off the test's thread too.
    private readonly ConcurrentDictionary<string, bool> _held = new(StringComparer.Ordinal);
    private WebFake<StorageManager>? _storage;

    public bool LocksSupported { get; set; } = true;

    /// <summary>Whether the origin is already exempt — an already-persisted origin must not be asked again.</summary>
    public bool AlreadyPersisted { get; set; }

    /// <summary>What the browser answers. False covers both "declined" and "no such API".</summary>
    public bool GrantsPersist { get; set; } = true;

    /// <summary>A refusal from <c>navigator.storage</c> itself.</summary>
    public JSException? StorageThrows { get; set; }

    public int PersistRequests => _storage?.Calls.Count(c => c.Member == "persist") ?? 0;

    /// <summary>Pre-hold a lock, standing in for another tab that already owns it.</summary>
    public void HoldElsewhere(string name) => _held[name] = true;

    /// <summary>Drop a pre-held lock — that other tab closing.</summary>
    public void ReleaseElsewhere(string name) => _held.TryRemove(name, out _);

    public bool IsHeld(string name) => _held.ContainsKey(name);

    /// <summary>Stands in for both, for the rest of the calling flow, until disposed of.</summary>
    public IDisposable Enter()
    {
        var locks = Navigator.Locks.Fake();
        locks.Returns(l => l.IsSupported, LocksSupported);
        locks.CallsBack<Lock?>("request", Request);

        _storage = Navigator.Storage.Fake();
        if (StorageThrows is { } refusal)
        {
            _storage.Throws(s => s.Persisted(), refusal);
        }
        else
        {
            _storage.Returns(s => s.Persisted(), AlreadyPersisted).Returns(s => s.Persist(), GrantsPersist);
        }

        return new Both(locks, _storage);
    }

    private async Task Request(WebCall call, Func<Lock?, Task> handler)
    {
        var name = (string)call.Args[0]!;
        if (!_held.TryAdd(name, true))
        {
            // Held: an ifAvailable request is handed no lock. A waiting one is not what the host makes.
            Assert.True(call.Args[1] is LockOptions { IfAvailable: true }, "the host never waits for a lock");
            await handler(null);
            return;
        }

        try
        {
            await handler(new Lock { Name = name, Mode = LockMode.Exclusive });
        }
        finally
        {
            _held.TryRemove(name, out _);
        }
    }

    private sealed class Both(IDisposable locks, IDisposable storage) : IDisposable
    {
        public void Dispose()
        {
            locks.Dispose();
            storage.Dispose();
        }
    }
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
