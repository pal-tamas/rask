using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server.Diagnostics;
using Rask.Server.Files;
using Rask.Server.JSInterop;

namespace Rask.Server;

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

    // How long the shutdown drain may take in total. Generous enough for a large fan-out, small enough to
    // leave the rest of the shutdown budget intact: the scaffolded host allows 15s and `rask deploy`
    // SIGKILLs 20s after SIGTERM, and what runs after this — disposing every session, checkpointing the
    // WAL, flushing a Litestream replica — is the part that loses data if it is cut short.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, LiveSession> _sessions = new();
    private readonly CancellationToken _stopping;

    // Cancelled once the shutdown drain has finished (or given up). Sockets abort on THIS, not on
    // ApplicationStopping: the drain has to reach clients while their sockets are still alive, and
    // CancellationToken callback order is not guaranteed, so registering the aborts on the same token the
    // drain runs from would be a coin flip. Also cancelled by DisposeAsync so a host with no lifetime
    // (tests, a hand-rolled host) still tears its sockets down.
    private readonly CancellationTokenSource _drained = new();

    /// <summary>
    ///     Cancelled when connected sockets should stop. See <see cref="_drained" /> for why this exists
    ///     rather than the sockets simply watching <c>ApplicationStopping</c>.
    /// </summary>
    public CancellationToken Drained => _drained.Token;

    // Atomic count of live + in-flight sessions, used by the hard capacity reservation in
    // TryCreate. Incremented BEFORE the component tree is built and decremented on removal (or
    // on a failed build), so a concurrent GET burst can never exceed MaxSessions.
    private int _liveCount;

    // 1 once DisposeAsync has run. See DisposeAsync for why it must be once-only.
    private int _disposed;

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
    }

    public int Count => _sessions.Count;

    /// <summary>
    ///     Reserved + in-flight + committed session count — the authoritative capacity number that
    ///     <see cref="TryCreate" /> / <see cref="AtCapacity" /> gate on. Differs from
    ///     <see cref="Count" /> (committed sessions only) during the window where a reserved session's
    ///     component tree is still building.
    /// </summary>
    internal int LiveCount => Volatile.Read(ref _liveCount);

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

    public async ValueTask DisposeAsync()
    {
        // Runs once. The store is a DI singleton, so the container disposes it — and a host or a test that
        // disposes it too would otherwise reach a Cancel() on an already-disposed token source.
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        CancelAllPending();

        // Backstop for a host that never raised ApplicationStopping — a test server, or one built without
        // an IHostApplicationLifetime. Sockets watch this token, so leaving it uncancelled would leave
        // their receive loops waiting on a store that no longer exists.
        _drained.Cancel();

        foreach (var key in _sessions.Keys.ToArray())
        {
            if (Detach(key) is { } session)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        _drained.Dispose();
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

    /// <summary>
    ///     Tells every connected client to reconnect now, then releases the sockets.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without this, a client discovers its host is gone only when the socket drops, and then walks
    ///         a backoff ladder — up to five seconds of a frozen page — before trying the replacement that
    ///         has already been serving the whole time. During a <c>rask deploy</c> the new container is
    ///         live and the proxy is pointed at it before the old one is stopped, so a client told to
    ///         reconnect immediately lands on the new host and (with a resume record) gets its page rebuilt
    ///         with no visible interruption at all.
    ///     </para>
    ///     <para>
    ///         Awaited by <see cref="LiveSessionDrainService" /> rather than run from an
    ///         <c>ApplicationStopping</c> callback. Those callbacks are synchronous, so reaching this from
    ///         one meant blocking a thread on the sends — and a host shutting down under load has a busy
    ///         thread pool, which is exactly when blocking on work that needs the pool to make progress
    ///         stops making progress. A hosted service's <c>StopAsync</c> is awaited properly and still
    ///         runs while the sockets are alive, because they now watch <see cref="Drained" />.
    ///     </para>
    ///     <para>
    ///         Bounded twice over — by <see cref="DrainTimeout" /> here and per-send by <c>SendTimeout</c>
    ///         — because everything that follows (disposing sessions, the WAL checkpoint, a Litestream
    ///         flush) is what actually loses data if the shutdown budget runs out.
    ///     </para>
    /// </remarks>
    internal async Task DrainAsync()
    {
        try
        {
            if (!_sessions.IsEmpty)
            {
                await BroadcastAsync(LivePayload.DrainFrame).WaitAsync(DrainTimeout).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            // Someone did not take their frame in time. Their loss is a reload; everyone else already has
            // theirs, and the rest of the shutdown budget belongs to the database now.
        }
        catch
        {
            // A faulted broadcast must never be the reason a host fails to shut down cleanly.
        }
        finally
        {
            // Whatever happened above, the sockets go now — they are waiting on this.
            _drained.Cancel();
        }
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

        var session = new LiveSession(sessionId, view, scope, DiffMode);
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
                    Component.MarkSubtreeDirtyForHotReload(session.View);
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
