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
    ///     Soft cap on concurrent sessions (<c>0</c> = unlimited). Set from
    ///     <see cref="Rask.Core.Live.RaskLiveOptions.MaxSessions" /> at registration. The
    ///     capacity gate is intentionally check-then-create rather than a hard atomic
    ///     reservation: a burst can admit a handful of sessions past the cap, which is fine
    ///     for a DoS backstop and keeps the common (uncapped) <see cref="Create" /> path free
    ///     of extra synchronisation. Tests set it directly on the resolved singleton.
    /// </summary>
    public int MaxSessions { get; set; }

    /// <summary>
    ///     True when a new session would exceed <see cref="MaxSessions" />. The GET endpoint
    ///     checks this before minting a session and returns 503 when it holds.
    /// </summary>
    public bool AtCapacity => MaxSessions > 0 && _sessions.Count >= MaxSessions;

    public async ValueTask DisposeAsync()
    {
        CancelAllPending();
        foreach (var key in _sessions.Keys.ToArray())
        {
            if (_sessions.TryRemove(key, out var session))
            {
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

    internal LiveSession Create(Func<IServiceProvider, Component> factory)
    {
        var scope = _scopeFactory.CreateScope();
        var sessionId = Guid.NewGuid().ToString("N");
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
            session.Dispose();
        }
    }

    internal async Task RemoveAsync(string id)
    {
        CancelPendingRemoval(id);
        if (_sessions.TryRemove(id, out var session))
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
        var prior = _pendingRemovals.AddOrUpdate(id, cts, (_, existing) =>
        {
            existing.Cancel();
            existing.Dispose();
            return cts;
        });
        if (!ReferenceEquals(prior, cts))
        {
            cts.Dispose();
            return;
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
