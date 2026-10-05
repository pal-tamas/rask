using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Rask.Core.Browser;
using Rask.Core.Forms;
using Rask.SQLite.Snapshots;
using Rask.Web;
using LockOptions = Rask.Web.Types.LockOptions;

namespace Rask.SQLite.Browser;

/// <summary>
///     Owns a browser SQLite database's lifetime: elects this tab as the owner, restores the file from
///     IndexedDB before anything opens it, and writes a final snapshot when the page goes away.
/// </summary>
/// <remarks>
///     <para>
///         A plain <see cref="IHostedService" />, not a <see cref="BackgroundService" />, and that is
///         load-bearing. <c>BackgroundService.StartAsync</c> returns the moment <c>ExecuteAsync</c> yields,
///         which would let the next hosted service — a job processor, say — open the database while the
///         restore was still in flight and find it empty. Doing the work inside <c>StartAsync</c> is what
///         makes "registered before it" mean "ready before it".
///     </para>
///     <para>
///         <b>One owner per origin.</b> Every tab has its own copy of the WASM runtime's in-memory
///         filesystem, so two tabs would hold two divergent databases and the last one to snapshot would
///         silently overwrite the other. A Web Lock elects exactly one owner; the others run with an empty
///         in-memory database that is never persisted, and say so in the log. Promoting a waiting tab when
///         the owner closes, or proxying its writes over a <c>BroadcastChannel</c>, is not implemented.
///     </para>
///     <para>
///         It starts at boot, outside any event handler, so it names the app's own services as the page its
///         <c>Navigator</c> calls run on; the lock and availability tasks it starts carry that with them.
///     </para>
/// </remarks>
internal sealed partial class BrowserSqliteHost(
    BrowserSqliteOptions options,
    IServiceProvider services,
    IIndexedDb indexedDb,
    ISqliteSnapshotter snapshotter,
    BrowserSqliteOwnership ownership,
    ILogger<BrowserSqliteHost> logger) : IHostedService, IDisposable
{
    // ifAvailable: a lock another tab holds is not waited for; the handler is handed none.
    private static readonly LockOptions IfAvailable = new() { IfAvailable = true };

    // Completing this releases the Web Lock: a lock is held only while the handler it was granted to runs,
    // so the handler parks on this until shutdown.
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IndexedDbSnapshotStore _store =
        new(indexedDb, BrowserSqlite.SnapshotStoreName(options.Name));

    // The in-flight lock request. For the owner it does not complete until _release is set, which is
    // exactly what holds the lock; awaiting it on shutdown is what makes "released" true by the time
    // StopAsync returns, rather than at some unobservable later moment.
    private Task? _ownerHold;

    // Stops the non-owner's availability watcher when the page goes away.
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _takeoverWatch;

    /// <summary>Whether this tab owns the database — i.e. whether it may persist anything.</summary>
    public bool IsOwner { get; private set; }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using (DispatchServicesScope.Push(services))
        {
            await StartOnPageAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StartOnPageAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath) ?? BrowserSqlite.DirectoryPath);

        IsOwner = await TryBecomeOwnerAsync().ConfigureAwait(false);

        // Published before the early return below, so a non-owner tab can say so in its UI instead of
        // rendering an empty page that reads as data loss.
        ownership.Resolve(IsOwner);

        if (!IsOwner)
        {
            LogNotOwner(logger, options.Name);

            // Not awaited: watching for the owner to go away must not hold up the boot.
            _takeoverWatch = WatchForAvailabilityAsync(_shutdown.Token);
            return;
        }

        if (options.RequestPersistentStorage)
        {
            await EnsurePersistentStorageAsync().ConfigureAwait(false);
        }

        await RestoreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Asks the browser not to evict this origin's storage.
    /// </summary>
    /// <remarks>
    ///     The snapshots this package writes live in IndexedDB, which is evictable: under storage pressure
    ///     a browser may discard them and the database returns empty next load, with nothing to say why.
    ///     A refusal changes nothing about how the app runs, so this never fails the boot — it only makes
    ///     the risk visible in the log instead of leaving it silent.
    ///     <para>
    ///         Checked before asked, so an origin that is already exempt never triggers a second prompt on
    ///         the browsers that prompt.
    ///     </para>
    /// </remarks>
    private async Task EnsurePersistentStorageAsync()
    {
        try
        {
            if (await Navigator.Storage.Persisted().ConfigureAwait(false))
            {
                return;
            }

            if (await Navigator.Storage.Persist().ConfigureAwait(false))
            {
                LogPersisted(logger, options.Name);
                return;
            }

            LogNotPersisted(logger, options.Name);
        }
        catch (JSException ex)
        {
            // Durability is best-effort: a browser without navigator.storage, or one that refuses, must not
            // stop the app booting.
            LogPersistFailed(logger, ex, options.Name);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Best-effort, and deliberately so: this runs from <c>pagehide</c>, which the browser does not
    ///     wait for. A snapshot that does not land costs whatever changed since the last interval tick —
    ///     which is the reason the interval exists rather than relying on this.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (IsOwner)
        {
            try
            {
                await snapshotter.Snapshot(cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // An unloading page has nothing to recover to; the last interval snapshot stands.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFinalSnapshotFailed(logger, ex, options.Name);
            }
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);

        if (_takeoverWatch is not null)
        {
            // Already swallows its own failures; awaiting only makes the stop orderly.
            await _takeoverWatch.ConfigureAwait(false);
        }

        _release.TrySetResult();

        if (_ownerHold is null)
        {
            return;
        }

        try
        {
            await _ownerHold.ConfigureAwait(false);
        }
        catch (JSException ex)
        {
            // The browser releases every lock when the context is torn down anyway.
            LogReleaseFailed(logger, ex, options.Name);
        }
    }

    /// <summary>
    ///     Watches, in a tab that is not the owner, for the database to become free.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Polls with an <c>ifAvailable</c> request, which acquires and releases within the call, rather
    ///         than waiting for the lock. Waiting would mean <em>holding</em> the
    ///         lock the moment it frees — and this tab must not own the database: it opened its own empty
    ///         one at boot, so persisting from here would overwrite the previous owner's good snapshot with
    ///         nothing. Holding a lock it must never use would also block a tab that could actually use it.
    ///     </para>
    ///     <para>
    ///         So this only ever <em>reports</em> availability, and the taking is done by a reload. That is
    ///         also why the signal is advisory: another tab may win between the poll and the reload.
    ///     </para>
    /// </remarks>
    private async Task WatchForAvailabilityAsync(CancellationToken cancellationToken)
    {
        var name = BrowserSqlite.OwnerLockName(options.Name);

        try
        {
            using var timer = new PeriodicTimer(options.TakeoverPollInterval);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                // Being handed the lock proves it is free, and returning at once hands it straight back.
                var free = false;
                await Navigator.Locks.Request(name, IfAvailable, granted => free = granted is not null).ConfigureAwait(false);
                if (free)
                {
                    LogAvailable(logger, options.Name);
                    ownership.MarkAvailable();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The page is going away.
        }
        catch (JSException ex)
        {
            // A watcher that dies must not take the app with it; the tab simply stops offering to take over.
            LogWatchFailed(logger, ex, options.Name);
        }
    }

    /// <summary>
    ///     Takes the owner lock and holds it for the lifetime of the page.
    /// </summary>
    /// <remarks>
    ///     A lock is held only while the handler it was granted to runs, so the handler parks on
    ///     <see cref="_release" />. That means the request itself never completes for the winner — hence
    ///     racing it against a signal raised from inside the handler rather than awaiting it.
    ///     <c>ifAvailable</c> rather than a waiting request: that has no cancellation here, so a second tab
    ///     would hang its whole boot until the first one closed.
    /// </remarks>
    private async Task<bool> TryBecomeOwnerAsync()
    {
        if (!await Navigator.Locks.IsSupported.ConfigureAwait(false))
        {
            // No Web Locks means no way to detect a second tab. Owning the database is the useful
            // behaviour for the overwhelmingly common single-tab case; the risk is stated rather than
            // silently taken.
            LogNoWebLocks(logger, options.Name);
            return true;
        }

        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _ownerHold = Navigator.Locks.Request(
            BrowserSqlite.OwnerLockName(options.Name),
            IfAvailable,
            async granted =>
            {
                if (granted is null)
                {
                    return; // another tab holds it
                }

                held.TrySetResult();
                await _release.Task.ConfigureAwait(false);
            }).AsTask();

        // Whichever happens first: the handler was granted the lock (we are the owner, and _ownerHold will
        // not complete until shutdown), or the request settled without one (someone else holds it).
        var first = await Task.WhenAny(held.Task, _ownerHold).ConfigureAwait(false);

        if (first == held.Task)
        {
            return true;
        }

        // Observe the completed request so a failure inside the interop call surfaces here rather than
        // as an unobserved task exception later. Nothing is holding a lock now, so nothing to release.
        await _ownerHold.ConfigureAwait(false);
        _ownerHold = null;
        return false;
    }

    /// <inheritdoc />
    public void Dispose() => _shutdown.Dispose();

    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var path = options.DatabasePath;

        // A database already on the in-memory filesystem means something opened it before the restore —
        // overwriting it would discard whatever it wrote. Only ever restore onto nothing.
        if (File.Exists(path))
        {
            LogAlreadyExists(logger, options.Name, path);
            return;
        }

        byte[]? bytes;
        try
        {
            bytes = await _store.ReadNewestAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A first run in a private window, or a cleared origin, must still boot.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogReadFailed(logger, ex, options.Name);
            return;
        }

        if (bytes is null)
        {
            LogNoSnapshot(logger, options.Name);
            return;
        }

        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        LogRestored(logger, options.Name, bytes.Length);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Another tab already owns the browser SQLite database '{Name}'. This tab starts with an empty "
                  + "in-memory database and will not persist anything, so two tabs cannot overwrite each other.")]
    private static partial void LogNotOwner(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Storage for '{Name}' is now exempt from eviction.")]
    private static partial void LogPersisted(ILogger logger, string name);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The browser did not grant persistent storage, so it may evict the snapshots of '{Name}' "
                  + "under storage pressure and the database would come back empty. Chromium grants this on "
                  + "engagement; Firefox prompts, so ask from a user gesture with "
                  + "Navigator.Storage.Persist() and set BrowserSqliteOptions."
                  + nameof(BrowserSqliteOptions.RequestPersistentStorage) + " to false.")]
    private static partial void LogNotPersisted(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not ask for persistent storage for '{Name}'.")]
    private static partial void LogPersistFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write a final snapshot of '{Name}' before the page unloaded.")]
    private static partial void LogFinalSnapshotFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Releasing the owner lock for '{Name}' failed.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "The tab that owned '{Name}' has gone; reload to use the database here.")]
    private static partial void LogAvailable(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gave up watching for '{Name}' to become available.")]
    private static partial void LogWatchFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "This browser has no Web Locks API, so a second tab cannot be detected. Database '{Name}' will be "
                  + "owned by every open tab, and the last one to snapshot wins.")]
    private static partial void LogNoWebLocks(ILogger logger, string name);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Browser SQLite database '{Name}' already exists at {Path} before restore; leaving it alone. "
                  + "Something opened the database before AddRaskBrowserSqlite's hosted service started.")]
    private static partial void LogAlreadyExists(ILogger logger, string name, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read a snapshot of '{Name}' from IndexedDB; starting empty.")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "No stored snapshot for '{Name}'; starting with an empty database.")]
    private static partial void LogNoSnapshot(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restored browser SQLite database '{Name}' ({Bytes} bytes).")]
    private static partial void LogRestored(ILogger logger, string name, int bytes);
}
