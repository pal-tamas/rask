using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Core;
using Rask.Server.Files;
using Rask.Server.JSInterop;

namespace Rask.Server;

public sealed class LiveSessionStore : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingRemovals = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, LiveSession> _sessions = new();
    private readonly CancellationToken _stopping;

    // Atomic count of live + in-flight sessions, used by the hard capacity reservation in
    // TryCreate. Incremented BEFORE the component tree is built and decremented on removal (or
    // on a failed build), so a concurrent GET burst can never exceed MaxSessions.
    private int _liveCount;

    public LiveSessionStore(IServiceScopeFactory scopeFactory, IHostApplicationLifetime? lifetime = null)
    {
        _scopeFactory = scopeFactory;
        _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
        if (lifetime is not null)
        {
            lifetime.ApplicationStopping.Register(CancelAllPending);
        }
    }

    public int Count => _sessions.Count;

    /// <summary>
    ///     Hard cap on concurrent sessions (<c>0</c> = unlimited). Set from
    ///     <see cref="Rask.Core.Live.RaskLiveOptions.MaxSessions" /> at registration. Enforced
    ///     atomically by <see cref="TryCreate" /> (a reservation taken before the component tree
    ///     is built), so a concurrent GET burst cannot exceed it. Tests set it directly on the
    ///     resolved singleton.
    /// </summary>
    public int MaxSessions { get; set; }

    /// <summary>
    ///     True when a new session would exceed <see cref="MaxSessions" />. A fast advisory
    ///     pre-check; the authoritative gate is <see cref="TryCreate" />'s atomic reservation.
    /// </summary>
    public bool AtCapacity => MaxSessions > 0 && Volatile.Read(ref _liveCount) >= MaxSessions;

    public async ValueTask DisposeAsync()
    {
        CancelAllPending();
        foreach (var key in _sessions.Keys.ToArray())
        {
            if (_sessions.TryRemove(key, out var session))
            {
                Interlocked.Decrement(ref _liveCount);
                await session.DisposeAsync().ConfigureAwait(false);
            }
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

        var session = new LiveSession(sessionId, view, scope);
        // Bind the per-scope LiveSessionAccessor so RaskJSRuntime (resolved from the same
        // scope) can find this session when components inject IJSRuntime.
        if (scope.ServiceProvider.GetService<LiveSessionAccessor>() is { } accessor)
        {
            accessor.Session = session;
        }

        _sessions[session.Id] = session;
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
        if (_sessions.TryRemove(id, out var session))
        {
            Interlocked.Decrement(ref _liveCount);
            session.Dispose();
        }
    }

    internal async Task RemoveAsync(string id)
    {
        CancelPendingRemoval(id);
        if (_sessions.TryRemove(id, out var session))
        {
            Interlocked.Decrement(ref _liveCount);
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

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
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

    public Task RerenderAllAsync()
    {
        if (_sessions.IsEmpty)
        {
            return Task.CompletedTask;
        }

        var tasks = new List<Task>(_sessions.Count);
        foreach (var session in _sessions.Values)
        {
            tasks.Add(session.View.StateHasChangedAsync());
        }

        return Task.WhenAll(tasks);
    }
}
