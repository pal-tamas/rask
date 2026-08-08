using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Core.Browser;
using Rask.SQLite.Snapshots;

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
///         the owner closes, or proxying its writes over <c>IBroadcastChannel</c>, is not implemented.
///     </para>
/// </remarks>
internal sealed class BrowserSqliteHost(
    BrowserSqliteOptions options,
    IWebLocks locks,
    IIndexedDb indexedDb,
    IStorageEstimator storage,
    ISqliteSnapshotter snapshotter,
    BrowserSqliteOwnership ownership,
    ILogger<BrowserSqliteHost> logger) : IHostedService
{
    // Completing this releases the Web Lock: IWebLocks holds the lock only for the lifetime of the
    // callback it is given, so the callback parks on this until shutdown.
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IndexedDbSnapshotStore _store =
        new(indexedDb, BrowserSqlite.SnapshotStoreName(options.Name));

    // The in-flight TryRequestAsync. For the owner it does not complete until _release is set, which is
    // exactly what holds the lock; awaiting it on shutdown is what makes "released" true by the time
    // StopAsync returns, rather than at some unobservable later moment.
    private Task<bool>? _ownerHold;

    // Stops the non-owner's availability watcher when the page goes away.
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _takeoverWatch;

    /// <summary>Whether this tab owns the database — i.e. whether it may persist anything.</summary>
    public bool IsOwner { get; private set; }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath) ?? BrowserSqlite.DirectoryPath);

        IsOwner = await TryBecomeOwnerAsync().ConfigureAwait(false);

        // Published before the early return below, so a non-owner tab can say so in its UI instead of
        // rendering an empty page that reads as data loss.
        ownership.Resolve(IsOwner);

        if (!IsOwner)
        {
            logger.LogWarning(
                "Another tab already owns the browser SQLite database '{Name}'. This tab starts with an empty "
                + "in-memory database and will not persist anything, so two tabs cannot overwrite each other.",
                options.Name);

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
            if (await storage.IsPersistedAsync().ConfigureAwait(false))
            {
                return;
            }

            if (await storage.RequestPersistAsync().ConfigureAwait(false))
            {
                logger.LogInformation(
                    "Storage for '{Name}' is now exempt from eviction.", options.Name);
                return;
            }

            // One branch, not two: RequestPersistAsync resolves false both when the browser declines and
            // when it has no such API, and from here those have exactly the same consequence.
            logger.LogWarning(
                "The browser did not grant persistent storage, so it may evict the snapshots of '{Name}' "
                + "under storage pressure and the database would come back empty. Chromium grants this on "
                + "engagement; Firefox prompts, so ask from a user gesture with "
                + "IStorageEstimator.RequestPersistAsync() and set BrowserSqliteOptions."
                + nameof(BrowserSqliteOptions.RequestPersistentStorage) + " to false.",
                options.Name);
        }
#pragma warning disable CA1031 // Durability is best-effort; a failed request must not stop the app booting.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Could not ask for persistent storage for '{Name}'.", options.Name);
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
                await snapshotter.SnapshotAsync(cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // An unloading page has nothing to recover to; the last interval snapshot stands.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogWarning(ex, "Could not write a final snapshot of '{Name}' before the page unloaded.", options.Name);
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
#pragma warning disable CA1031 // The browser releases every lock when the context is torn down anyway.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Releasing the owner lock for '{Name}' failed.", options.Name);
        }
    }

    /// <summary>
    ///     Watches, in a tab that is not the owner, for the database to become free.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Polls with <see cref="IWebLocks.TryRequestAsync" />, which acquires and releases within the
    ///         call, rather than waiting on <c>RequestAsync</c>. Waiting would mean <em>holding</em> the
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
                // The callback is empty on purpose: acquiring proves the lock is free, and returning
                // immediately hands it straight back.
                if (await locks.TryRequestAsync(name, static () => Task.CompletedTask).ConfigureAwait(false))
                {
                    logger.LogInformation(
                        "The tab that owned '{Name}' has gone; reload to use the database here.", options.Name);
                    ownership.MarkAvailable();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The page is going away.
        }
#pragma warning disable CA1031 // A watcher that dies must not take the app with it; the tab simply stops offering to take over.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Gave up watching for '{Name}' to become available.", options.Name);
        }
    }

    /// <summary>
    ///     Takes the owner lock and holds it for the lifetime of the page.
    /// </summary>
    /// <remarks>
    ///     <see cref="IWebLocks.TryRequestAsync" /> holds the lock only while its callback runs, so the
    ///     callback parks on <see cref="_release" />. That means the call itself never completes for the
    ///     winner — hence racing it against a signal raised from inside the callback rather than awaiting
    ///     it. <c>TryRequestAsync</c> and not <c>RequestAsync</c>: the waiting form has no cancellation, so
    ///     a second tab would hang its whole boot until the first one closed.
    /// </remarks>
    private async Task<bool> TryBecomeOwnerAsync()
    {
        if (!await locks.IsSupportedAsync().ConfigureAwait(false))
        {
            // No Web Locks means no way to detect a second tab. Owning the database is the useful
            // behaviour for the overwhelmingly common single-tab case; the risk is stated rather than
            // silently taken.
            logger.LogWarning(
                "This browser has no Web Locks API, so a second tab cannot be detected. Database '{Name}' will be "
                + "owned by every open tab, and the last one to snapshot wins.",
                options.Name);
            return true;
        }

        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _ownerHold = locks.TryRequestAsync(
            BrowserSqlite.OwnerLockName(options.Name),
            async () =>
            {
                held.TrySetResult();
                await _release.Task.ConfigureAwait(false);
            }).AsTask();

        // Whichever happens first: the callback started (we are the owner, and _ownerHold will not
        // complete until shutdown), or the request returned false (someone else holds it).
        var first = await Task.WhenAny(held.Task, _ownerHold).ConfigureAwait(false);

        if (first == held.Task)
        {
            return true;
        }

        // Observe the completed request so a failure inside the interop call surfaces here rather than
        // as an unobserved task exception later. Nothing is holding a lock now, so nothing to release.
        var granted = await _ownerHold.ConfigureAwait(false);
        _ownerHold = null;
        return granted;
    }

    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var path = options.DatabasePath;

        // A database already on the in-memory filesystem means something opened it before the restore —
        // overwriting it would discard whatever it wrote. Only ever restore onto nothing.
        if (File.Exists(path))
        {
            logger.LogWarning(
                "Browser SQLite database '{Name}' already exists at {Path} before restore; leaving it alone. "
                + "Something opened the database before AddRaskBrowserSqlite's hosted service started.",
                options.Name,
                path);
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
            logger.LogWarning(ex, "Could not read a snapshot of '{Name}' from IndexedDB; starting empty.", options.Name);
            return;
        }

        if (bytes is null)
        {
            logger.LogInformation("No stored snapshot for '{Name}'; starting with an empty database.", options.Name);
            return;
        }

        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Restored browser SQLite database '{Name}' ({Bytes} bytes).", options.Name, bytes.Length);
    }
}
