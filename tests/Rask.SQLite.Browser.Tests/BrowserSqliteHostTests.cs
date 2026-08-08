using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Rask.SQLite.Browser.Tests;

public sealed class BrowserSqliteHostTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("rask-browser-sqlite").FullName;
    private readonly FakeIndexedDb _db = new();
    private readonly FakeWebLocks _locks = new();
    private readonly RecordingSnapshotter _snapshotter = new();

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private BrowserSqliteOptions Options(string name = "app")
    {
        var options = new BrowserSqliteOptions
        {
            Name = name,
            DatabasePath = Path.Combine(_temp, $"{name}.db"),
            // Short, so the takeover tests do not spend seconds waiting on a poll.
            TakeoverPollInterval = TimeSpan.FromMilliseconds(10),
        };
        options.Validate();
        return options;
    }

    private readonly BrowserSqliteOwnership _ownership = new();
    private readonly FakeStorageEstimator _storage = new();

    private BrowserSqliteHost Host(BrowserSqliteOptions options) =>
        new(options, _locks, _db, _storage, _snapshotter, _ownership, NullLogger<BrowserSqliteHost>.Instance);

    private void Seed(string name, string snapshotName, string content) =>
        _db.Store(BrowserSqlite.SnapshotStoreName(name)).Values[snapshotName] = Encoding.UTF8.GetBytes(content);

    [Fact]
    public async Task Start_FreeLock_BecomesTheOwner()
    {
        var host = Host(Options());

        await host.StartAsync(CancellationToken.None);

        Assert.True(host.IsOwner);
        await host.StopAsync(CancellationToken.None);
    }

    // Two tabs each hold their own copy of the in-memory filesystem, so a second owner would mean two
    // divergent databases and a last-writer-wins overwrite.
    [Fact]
    public async Task Start_LockHeldByAnotherTab_DoesNotBecomeTheOwner()
    {
        var options = Options();
        _locks.HoldElsewhere(BrowserSqlite.OwnerLockName(options.Name));
        var host = Host(options);

        await host.StartAsync(CancellationToken.None);

        Assert.False(host.IsOwner);
    }

    // Without this an app cannot tell the user why its data is missing, and an empty page reads as
    // deletion rather than as another tab holding the database.
    [Fact]
    public async Task Start_PublishesOwnership()
    {
        var host = Host(Options());

        Assert.Null(_ownership.IsOwner);   // undecided before the election, not "false"

        await host.StartAsync(CancellationToken.None);

        Assert.True(_ownership.IsOwner);
        Assert.True(await _ownership.Resolved);
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_NonOwner_PublishesOwnershipToo()
    {
        var options = Options();
        _locks.HoldElsewhere(BrowserSqlite.OwnerLockName(options.Name));

        await Host(options).StartAsync(CancellationToken.None);

        // Published before the early return the non-owner path takes — that is the whole point.
        Assert.False(_ownership.IsOwner);
        Assert.False(await _ownership.Resolved);
    }

    [Fact]
    public async Task Start_NonOwner_DoesNotRestore()
    {
        var options = Options();
        Seed(options.Name, "app-20260808-120000000.db", "restored");
        _locks.HoldElsewhere(BrowserSqlite.OwnerLockName(options.Name));

        await Host(options).StartAsync(CancellationToken.None);

        Assert.False(File.Exists(options.DatabasePath));
    }

    // No Web Locks means a second tab cannot be detected at all; owning the database is the useful
    // behaviour for the single-tab case that is overwhelmingly the common one.
    [Fact]
    public async Task Start_WebLocksUnsupported_BecomesTheOwner()
    {
        _locks.Supported = false;
        var host = Host(Options());

        await host.StartAsync(CancellationToken.None);

        Assert.True(host.IsOwner);
        await host.StopAsync(CancellationToken.None);
    }

    // IndexedDB is evictable, so without asking, a browser under storage pressure can discard the
    // snapshots and the database comes back empty with nothing to say why.
    [Fact]
    public async Task Start_AsksForPersistentStorage()
    {
        var host = Host(Options());

        await host.StartAsync(CancellationToken.None);

        Assert.Equal(1, _storage.PersistRequests);
        await host.StopAsync(CancellationToken.None);
    }

    // Asking again would prompt a second time on the browsers that prompt, for something already granted.
    [Fact]
    public async Task Start_AlreadyPersisted_DoesNotAskAgain()
    {
        _storage.AlreadyPersisted = true;
        var host = Host(Options());

        await host.StartAsync(CancellationToken.None);

        Assert.Equal(0, _storage.PersistRequests);
        await host.StopAsync(CancellationToken.None);
    }

    // A refusal changes nothing about how the app runs — it must not fail the boot, only be visible.
    [Fact]
    public async Task Start_PersistDeclined_StillBootsAndRestores()
    {
        _storage.GrantsPersist = false;
        var options = Options();
        Seed(options.Name, "app-20260808-140000000.db", "restored");
        var host = Host(options);

        await host.StartAsync(CancellationToken.None);

        Assert.True(host.IsOwner);
        Assert.Equal("restored", await File.ReadAllTextAsync(options.DatabasePath));
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_PersistThrows_StillBoots()
    {
        _storage.Throws = new InvalidOperationException("interop failed");
        var host = Host(Options());

        await host.StartAsync(CancellationToken.None);

        Assert.True(host.IsOwner);
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_PersistDisabled_NeverAsks()
    {
        var options = Options();
        options.RequestPersistentStorage = false;
        var host = Host(options);

        await host.StartAsync(CancellationToken.None);

        Assert.Equal(0, _storage.PersistRequests);
        await host.StopAsync(CancellationToken.None);
    }

    // A non-owner persists nothing, so a prompt there would buy the user nothing.
    [Fact]
    public async Task Start_NonOwner_NeverAsksForPersistentStorage()
    {
        var options = Options();
        _locks.HoldElsewhere(BrowserSqlite.OwnerLockName(options.Name));

        await Host(options).StartAsync(CancellationToken.None);

        Assert.Equal(0, _storage.PersistRequests);
    }

    // A second tab is otherwise stuck: told to close the other one, with no way to know when that
    // happened. This is the signal that turns "close the other tab" into "reload now".
    [Fact]
    public async Task NonOwner_SignalsWhenTheDatabaseBecomesAvailable()
    {
        var options = Options();
        var lockName = BrowserSqlite.OwnerLockName(options.Name);
        _locks.HoldElsewhere(lockName);
        var host = Host(options);

        await host.StartAsync(CancellationToken.None);

        Assert.False(_ownership.Available.IsCompleted);   // the owner is still there

        _locks.ReleaseElsewhere(lockName);               // that tab closes

        await _ownership.Available.WaitAsync(TimeSpan.FromSeconds(5));
        await host.StopAsync(CancellationToken.None);
    }

    // Reporting availability must not take the lock: this tab already opened its own EMPTY database, so
    // owning it would mean snapshotting nothing over the previous owner's good snapshot.
    [Fact]
    public async Task NonOwner_DoesNotHoldTheLockItReportsAsFree()
    {
        var options = Options();
        var lockName = BrowserSqlite.OwnerLockName(options.Name);
        _locks.HoldElsewhere(lockName);
        var host = Host(options);
        await host.StartAsync(CancellationToken.None);

        _locks.ReleaseElsewhere(lockName);
        await _ownership.Available.WaitAsync(TimeSpan.FromSeconds(5));

        // Free for a tab that can actually use it — a reloaded page, or another tab.
        Assert.DoesNotContain(await _locks.QueryAsync(), l => l.Name == lockName);
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Owner_NeverSignalsAvailability()
    {
        var host = Host(Options());

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(60);

        // The owner has nothing to wait for; completing here would tell an app to reload for no reason.
        Assert.False(_ownership.Available.IsCompleted);
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stop_EndsTheAvailabilityWatch()
    {
        var options = Options();
        var lockName = BrowserSqlite.OwnerLockName(options.Name);
        _locks.HoldElsewhere(lockName);
        var host = Host(options);
        await host.StartAsync(CancellationToken.None);

        // Returns only once the watcher has stopped — a watcher still polling after shutdown would hang it.
        await host.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        _locks.ReleaseElsewhere(lockName);
        await Task.Delay(60);
        Assert.False(_ownership.Available.IsCompleted);
    }

    [Fact]
    public async Task Start_RestoresTheNewestSnapshot()
    {
        var options = Options();
        Seed(options.Name, "app-20260808-120000000.db", "old");
        Seed(options.Name, "app-20260808-140000000.db", "newest");
        var host = Host(options);

        await host.StartAsync(CancellationToken.None);

        Assert.Equal("newest", await File.ReadAllTextAsync(options.DatabasePath));
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_NoSnapshot_LeavesNoDatabaseFile()
    {
        var options = Options();
        var host = Host(options);

        await host.StartAsync(CancellationToken.None);

        // Absent, not empty: a zero-byte file is not a valid SQLite database, and SQLite creates a real
        // one on first open.
        Assert.False(File.Exists(options.DatabasePath));
        await host.StopAsync(CancellationToken.None);
    }

    // Restoring over a file something already opened would discard whatever it had written.
    [Fact]
    public async Task Start_DatabaseAlreadyExists_DoesNotOverwriteIt()
    {
        var options = Options();
        await File.WriteAllTextAsync(options.DatabasePath, "live");
        Seed(options.Name, "app-20260808-140000000.db", "snapshot");
        var host = Host(options);

        await host.StartAsync(CancellationToken.None);

        Assert.Equal("live", await File.ReadAllTextAsync(options.DatabasePath));
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stop_Owner_WritesAFinalSnapshot()
    {
        var host = Host(Options());
        await host.StartAsync(CancellationToken.None);

        await host.StopAsync(CancellationToken.None);

        Assert.Equal(1, _snapshotter.Count);
    }

    [Fact]
    public async Task Stop_NonOwner_WritesNothing()
    {
        var options = Options();
        _locks.HoldElsewhere(BrowserSqlite.OwnerLockName(options.Name));
        var host = Host(options);
        await host.StartAsync(CancellationToken.None);

        await host.StopAsync(CancellationToken.None);

        Assert.Equal(0, _snapshotter.Count);
    }

    // pagehide gives no time guarantee, so a failed final snapshot must not throw out of shutdown —
    // the last interval snapshot is the fallback.
    [Fact]
    public async Task Stop_SnapshotThrows_DoesNotPropagate()
    {
        var host = Host(Options());
        await host.StartAsync(CancellationToken.None);
        _snapshotter.Throws = new IOException("quota exceeded");

        await host.StopAsync(CancellationToken.None);
    }

    // The owner holds the Web Lock for the page's whole lifetime, so it must be released on shutdown or
    // a same-origin context could never take it.
    [Fact]
    public async Task Stop_ReleasesTheOwnerLock()
    {
        var options = Options();
        var host = Host(options);
        await host.StartAsync(CancellationToken.None);

        Assert.Contains(await _locks.QueryAsync(), l => l.Name == BrowserSqlite.OwnerLockName(options.Name));

        await host.StopAsync(CancellationToken.None);

        Assert.DoesNotContain(await _locks.QueryAsync(), l => l.Name == BrowserSqlite.OwnerLockName(options.Name));
    }
}
