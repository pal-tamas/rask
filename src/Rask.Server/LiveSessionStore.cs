using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server.Diagnostics;
using Rask.Server.Files;
using Rask.Server.JSInterop;

namespace Rask.Server;

/// <summary>
///     Every live session on this host: the component tree, DI scope and pending work behind each
///     connected browser. Registered by <c>AddRask()</c> and driven by the WebSocket endpoint, so an app
///     rarely touches it directly — reach for it to observe how many sessions exist, or to fan work out
///     across them.
/// </summary>
/// <remarks>
///     Sessions are server-held state and therefore a capacity limit rather than an unbounded resource:
///     admission is reserved before a tree is built, so a burst of connections cannot exceed the
///     configured maximum. A rejected session is counted, not queued.
/// </remarks>
public sealed class LiveSessionStore : IAsyncDisposable
{
    private readonly RaskMetrics? _metrics;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingRemovals = new();
    private readonly IServiceScopeFactory _scopeFactory;
    // How many sessions a whole-store fan-out touches at once. Bounded so a host with thousands of
    // sessions doesn't hand the thread pool thousands of simultaneous socket writes; large enough that a
    // handful of slow clients can't dominate. Both fan-outs are dev-time today, but this is the code the
    // planned Broadcast pillar inherits, where the fan-out is a user-facing feature.
    private const int FanOutConcurrency = 32;

    private readonly ConcurrentDictionary<string, LiveSession> _sessions = new();
    private readonly CancellationToken _stopping;

    // Atomic count of live + in-flight sessions, used by the hard capacity reservation in
    // TryCreate. Incremented BEFORE the component tree is built and decremented on removal (or
    // on a failed build), so a concurrent GET burst can never exceed MaxSessions.
    private int _liveCount;

    // 1 once DisposeAsync has run. See DisposeAsync for why it must be once-only.
    private int _disposed;

    // Sessions with a socket attached, and handler dispatches queued across all of them. See the
    // properties below for why each is tracked here rather than derived on scrape.
    private int _connectedCount;
    private int _pendingHandlerCount;

    /// <summary>Creates the store. <c>AddRask()</c> registers it; construct one directly only in tests.</summary>
    /// <param name="scopeFactory">Creates the DI scope each session owns.</param>
    /// <param name="lifetime">The host lifetime, so sessions drain on shutdown instead of being cut off.</param>
    /// <param name="metrics">Where session counters are published, when metrics are enabled.</param>
    public LiveSessionStore(
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime? lifetime = null,
        RaskMetrics? metrics = null)
    {
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
        if (lifetime is not null)
        {
            lifetime.ApplicationStopping.Register(CancelAllPending);
        }

        // Active-session gauge: the collector polls on scrape. Reads LiveCount (reservations +
        // committed) — the same number admission and the health check gate on — so the gauge, the
        // capacity cap, and /health never disagree by the count of mid-build sessions.
        _metrics?.TrackActiveSessions(() => LiveCount);
        _metrics?.TrackConnectedSessions(() => ConnectedCount);
        _metrics?.TrackPendingHandlers(() => PendingHandlerCount);
    }

    /// <summary>
    ///     How many sessions exist right now, connected or awaiting reconnection. Compare with
    ///     <see cref="AtCapacity" /> to tell "busy" from "full".
    /// </summary>
    public int Count => _sessions.Count;

    /// <summary>
    ///     The host's shutdown state, set by <c>AddRask</c> right after construction. Assigned rather than
    ///     injected because this type's constructor is public while <see cref="RaskDrainCoordinator" /> is
    ///     internal — and because leaving it null keeps every directly-constructed test store working
    ///     unchanged (it simply never drains).
    /// </summary>
    internal RaskDrainCoordinator? Drain { get; set; }

    /// <summary>True while the host is shutting down: admission is closed and the drain owns teardown.</summary>
    internal bool IsDraining => Drain?.IsDraining == true;

    /// <summary>
    ///     A point-in-time snapshot of the live sessions. <c>ConcurrentDictionary.Values</c> already
    ///     copies, which is what the broadcast path relies on, so the drain can iterate while the socket
    ///     loops are still removing themselves.
    /// </summary>
    internal ICollection<LiveSession> Snapshot() => _sessions.Values;

    /// <summary>Ids of the live sessions, for a teardown loop that must tolerate concurrent removal.</summary>
    internal ICollection<string> SessionIds() => _sessions.Keys;

    /// <summary>
    ///     Reserved + in-flight + committed session count — the authoritative capacity number that
    ///     <see cref="TryCreate" /> / <see cref="AtCapacity" /> gate on. Differs from
    ///     <see cref="Count" /> (committed sessions only) during the window where a reserved session's
    ///     component tree is still building.
    /// </summary>
    internal int LiveCount => Volatile.Read(ref _liveCount);

    /// <summary>
    ///     Sessions with a socket attached right now — the number of people actually looking at the app.
    /// </summary>
    /// <remarks>
    ///     Distinct from <see cref="LiveCount" />, which also counts sessions minted by a bare <c>GET</c>
    ///     whose WebSocket never arrived and sessions riding out their reconnect grace. Those hold
    ///     capacity, so admission is right to count them — but they are not users, and a host cannot tell
    ///     "filling up with real traffic" from "filling up with probes and dropped connections" if the two
    ///     numbers are the same number.
    /// </remarks>
    public int ConnectedCount => Volatile.Read(ref _connectedCount);

    /// <summary>Action dispatches queued across every session — the backpressure breaker's input.</summary>
    /// <remarks>
    ///     Only the <em>rejections</em> were observable before: you could see the breaker trip and had no
    ///     way to see it coming. A store-level counter rather than a sum over sessions on scrape, because
    ///     scraping is not the moment to walk tens of thousands of them.
    /// </remarks>
    public int PendingHandlerCount => Volatile.Read(ref _pendingHandlerCount);

    internal void SocketAttached() => Interlocked.Increment(ref _connectedCount);

    internal void SocketDetached() => Interlocked.Decrement(ref _connectedCount);

    internal void HandlerQueued() => Interlocked.Increment(ref _pendingHandlerCount);

    internal void HandlerDequeued() => Interlocked.Decrement(ref _pendingHandlerCount);

    /// <summary>The metrics sink threaded into the WS loop for frame/handler instrumentation (may be null).</summary>
    internal RaskMetrics? Metrics => _metrics;

    /// <summary>
    ///     Hard cap on concurrent sessions (<c>0</c> = unlimited). Set from
    ///     <see cref="Rask.Core.Live.RaskLiveOptions.MaxSessions" /> at registration. Enforced
    ///     atomically by <see cref="TryCreate" /> (a reservation taken before the component tree
    ///     is built), so a concurrent GET burst cannot exceed it. Tests set it directly on the
    ///     resolved singleton.
    /// </summary>
    public int MaxSessions { get; set; }

    /// <summary>
    ///     The wire-payload shape (<see cref="LiveDiffMode" />) every session this store mints is
    ///     built with. Seeded once from <see cref="Rask.Core.Live.RaskLiveOptions.DiffMode" /> at
    ///     registration and handed to each <see cref="LiveSession" /> at construction — a per-host
    ///     value, not a process-global static, so concurrent hosts and parallel tests each render in
    ///     their own mode. Defaults to <see cref="LiveDiffMode.Auto" /> (diff codec on).
    /// </summary>
    public LiveDiffMode DiffMode { get; init; } = LiveDiffMode.Auto;

    /// <summary>
    ///     True when a new session would exceed <see cref="MaxSessions" />. A fast advisory
    ///     pre-check; the authoritative gate is <see cref="TryCreate" />'s atomic reservation.
    /// </summary>
    public bool AtCapacity => MaxSessions > 0 && Volatile.Read(ref _liveCount) >= MaxSessions;

    /// <summary>
    ///     Tears down every session and its DI scope. Runs once — repeat calls are no-ops — so a host
    ///     shutting down cannot dispose the same session twice.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Runs once. The store is a DI singleton, so the container disposes it — and a host or a test that
        // disposes it too would otherwise reach a Cancel() on an already-disposed token source. Salvaged
        // from #572, which found it; the rest of that PR is superseded by this drain.
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        CancelAllPending();
        foreach (var key in _sessions.Keys.ToArray())
        {
            if (Detach(key) is { } session)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    // The single removal point all three paths (Remove / RemoveAsync / DisposeAsync) funnel through:
    // atomically take the session out of the map, drop the live-count reservation, and record the
    // eviction metric exactly once. Returns the detached session for the caller to dispose (sync or
    // async), or null if another thread already removed it.
    private LiveSession? Detach(string id)
    {
        if (_sessions.TryRemove(id, out var session))
        {
            Interlocked.Decrement(ref _liveCount);
            _metrics?.SessionEvicted();
            return session;
        }

        return null;
    }

    private void CancelAllPending()
    {
        foreach (var key in _pendingRemovals.Keys.ToArray())
        {
            if (_pendingRemovals.TryRemove(key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }

    /// <summary>
    ///     Creates a session unconditionally (no capacity check). Used internally and by tests.
    ///     The GET endpoint uses <see cref="TryCreate" /> so the cap is enforced.
    /// </summary>
    internal LiveSession Create(Func<IServiceProvider, Component> factory)
    {
        Interlocked.Increment(ref _liveCount);
        try
        {
            return CreateCore(factory);
        }
        catch
        {
            Interlocked.Decrement(ref _liveCount);
            throw;
        }
    }

    /// <summary>
    ///     Atomically reserves a capacity slot, then creates the session. Returns <c>null</c>
    ///     (reserving nothing) when the session would exceed <see cref="MaxSessions" /> — the
    ///     reservation is taken BEFORE the component tree is built, so a burst of concurrent GETs
    ///     can never push past the cap (the old check-then-create gate could admit a handful).
    /// </summary>
    internal LiveSession? TryCreate(Func<IServiceProvider, Component> factory)
    {
        // Refuse before reserving anything: a session minted during the drain is one the drain has
        // already snapshotted past, so it would be built (component tree + DI scope) only to be torn
        // down moments later — and the client would be handed a page whose session is already dead.
        if (IsDraining)
        {
            _metrics?.SessionRejected();
            return null;
        }

        var reserved = Interlocked.Increment(ref _liveCount);
        if (MaxSessions > 0 && reserved > MaxSessions)
        {
            Interlocked.Decrement(ref _liveCount);
            _metrics?.SessionRejected();
            return null;
        }

        try
        {
            return CreateCore(factory);
        }
        catch
        {
            Interlocked.Decrement(ref _liveCount);
            throw;
        }
    }

    private LiveSession CreateCore(Func<IServiceProvider, Component> factory)
    {
        var scope = _scopeFactory.CreateScope();
        // Cryptographically-random id: it is the bearer secret for the WS / upload / download
        // endpoints (see SecureToken), so it must not be a v4 GUID.
        var sessionId = SecureToken.Create();
        if (scope.ServiceProvider.GetService<RaskSessionContext>() is { } sessionCtx)
        {
            sessionCtx.Id = sessionId;
        }

        Component view;
        try
        {
            view = factory(scope.ServiceProvider);
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        var session = new LiveSession(sessionId, view, scope, DiffMode, _metrics);
        // Bind the per-scope LiveSessionAccessor so RaskJSRuntime (resolved from the same
        // scope) can find this session when components inject IJSRuntime.
        if (scope.ServiceProvider.GetService<LiveSessionAccessor>() is { } accessor)
        {
            accessor.Session = session;
        }

        _sessions[session.Id] = session;
        _metrics?.SessionCreated();
        return session;
    }

    internal LiveSession? Get(string id)
    {
        CancelPendingRemoval(id);
        return _sessions.TryGetValue(id, out var session) ? session : null;
    }

    internal void Remove(string id)
    {
        CancelPendingRemoval(id);
        Detach(id)?.Dispose();
    }

    internal async Task RemoveAsync(string id)
    {
        CancelPendingRemoval(id);
        if (Detach(id) is { } session)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal void ScheduleRemoval(string id, TimeSpan delay)
    {
        // Draining: the drain owns teardown and disposes every session awaited, inside the shutdown
        // window. Doing anything here would be worse than nothing — this used to fire off an unawaited
        // RemoveAsync, so a component's async unmount raced process exit with nobody observing it.
        if (IsDraining)
        {
            return;
        }

        if (_stopping.IsCancellationRequested)
        {
            _ = RemoveAsync(id);
            return;
        }

        if (!_sessions.ContainsKey(id))
        {
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
        // Install the new CTS, retiring any prior pending removal for this id. A CTS must
        // never be cancelled/disposed while it is still reachable through _pendingRemovals:
        // a concurrent CancelPendingRemoval / CancelAllPending could TryRemove the same
        // instance and race us into a double-dispose (ObjectDisposedException out of Cancel).
        // So swap atomically and retire the prior CTS only after our CAS has made it
        // unreachable — never from inside an AddOrUpdate factory, whose side effects mutate a
        // value other threads can still observe in the dictionary. The thread whose
        // TryUpdate/TryRemove wins is the single owner responsible for disposing that value.
        while (true)
        {
            if (_pendingRemovals.TryGetValue(id, out var existing))
            {
                if (_pendingRemovals.TryUpdate(id, cts, existing))
                {
                    existing.Cancel();
                    existing.Dispose();
                    break;
                }
            }
            else if (_pendingRemovals.TryAdd(id, cts))
            {
                break;
            }
        }

        // Capture the token here, on the calling thread, while this CTS is freshly created and
        // provably not disposed. Reading cts.Token *inside* the task instead would race a
        // concurrent CancelPendingRemoval / retire that disposes this CTS first — the getter
        // then throws ObjectDisposedException, which the OperationCanceledException catch below
        // would miss, surfacing as an unobserved task exception.
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                // The CTS was retired (a newer ScheduleRemoval) or cancelled and disposed
                // concurrently. Either way this removal is obsolete — the owning thread handles it.
                return;
            }

            var entry = new KeyValuePair<string, CancellationTokenSource>(id, cts);
            if (!((ICollection<KeyValuePair<string, CancellationTokenSource>>)_pendingRemovals).Remove(entry))
            {
                return;
            }

            cts.Dispose();
            await RemoveAsync(id).ConfigureAwait(false);
        });
    }

    private void CancelPendingRemoval(string id)
    {
        if (_pendingRemovals.TryRemove(id, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>
    ///     Re-render every live session from scratch. Used by the dev-time asset-change subscriber.
    ///     <para>
    ///         Marks each tree dirty before requesting the render: <c>StateHasChangedAsync</c> alone
    ///         only dirties the root, so a cached child subtree replays its previous frame and an edit
    ///         inside it never appears. Costs nothing in production — the only thing that raises
    ///         <c>AssetChanged</c> outside hot reload is a module initializer at startup, before any
    ///         session exists, which the empty check below short-circuits.
    ///     </para>
    ///     <para>Best-effort: a session whose tree walk or render faults is skipped, not propagated.</para>
    /// </summary>
    public async Task RerenderAllAsync()
    {
        if (_sessions.IsEmpty)
        {
            return;
        }

        // Bounded-concurrent for the same reason as BroadcastAsync: sequentially, one session stuck on a
        // send holds up every session behind it. Rendering distinct sessions on distinct threads is not a
        // new property — independent handler dispatches already do it — because each session serialises
        // itself on its own render lock and the render walk's ambient scopes are thread-static.
        await Parallel.ForEachAsync(
            _sessions.Values,
            new ParallelOptions { MaxDegreeOfParallelism = FanOutConcurrency },
            async (session, _) =>
            {
                try
                {
                    Component.MarkSubtreeDirtyInternal(session.View);
                    await session.View.StateHasChangedAsync().ConfigureAwait(false);
                }
                catch
                {
                    // One bad tree must not stop the rest — matches RerenderAllForHotReloadAsync.
                }
            }).ConfigureAwait(false);
    }

    /// <summary>
    ///     Pushes a pre-encoded control frame to every connected session. Used for the dev-only
    ///     "hot reload applied" signal; the payload rides the existing socket through
    ///     <see cref="LiveSession.SendOutOfBandAsync" />, which takes the render lock so it cannot
    ///     interleave with an in-flight frame. Detached sessions are skipped.
    /// </summary>
    public async Task BroadcastAsync(ReadOnlyMemory<byte> payload)
    {
        if (_sessions.IsEmpty)
        {
            return;
        }

        // Concurrently, with a bounded degree. Sequentially, the cost of a broadcast is the SUM of every
        // session's send rather than the slowest one — and each of those sends waits on that session's
        // render lock behind whatever frame is already in flight. One session on a stalled link would
        // hold up delivery to everyone behind it in the dictionary's enumeration order. The bound keeps a
        // large fan-out from saturating the pool; a session whose send faults is skipped, not propagated.
        await Parallel.ForEachAsync(
            _sessions.Values,
            new ParallelOptions { MaxDegreeOfParallelism = FanOutConcurrency },
            async (session, _) =>
            {
                try
                {
                    await session.SendOutOfBandAsync(payload).ConfigureAwait(false);
                }
                catch
                {
                    // A closed/faulted socket must not stop the broadcast.
                }
            }).ConfigureAwait(false);
    }
}
