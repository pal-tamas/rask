using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;

namespace Rask.Core.Live;

/// <summary>
///     The services of the live event in progress: the app's scoped services from a scope of the event's own, opened
///     on first use, and everything else from the session.
/// </summary>
/// <remarks>
///     <para>
///         One per session, so making it ambient allocates nothing: an event that resolves nothing of the app's and
///         leaves nothing running opens no scope and allocates nothing.
///     </para>
///     <para>
///         An event's scope lives as long as the work the event started: the event itself, every lifecycle hook its
///         renders started and every read its page went out without. A hook goes on long after a navigation has been
///         sent — what it resolves after an await is still its own event's, found again by the render that started
///         it, and is disposed when the last of that work is done. A hook whose component has left the page keeps it
///         for <see cref="TeardownGrace" /> more at most; the session's end disposes every one.
///     </para>
/// </remarks>
internal sealed class EventServices(LiveSessionBase session, EventScopeCatalog catalog, IServiceScopeFactory scopes)
    : IKeyedServiceProvider
{
    /// <summary>How long a hook whose component has left the page keeps its event's services.</summary>
    internal static readonly TimeSpan DefaultTeardownGrace = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();

    // The event in progress, or null while it has resolved nothing and started nothing that runs on.
    private EventLife? _current;

    // Whether there is an event in progress to end, read without the gate by the end of every event: every event's path.
    private volatile bool _open;

    // The render that started a hook still running, to the event it belongs to: what the hook's continuations carry
    // in their flow, and so how one that resolves something after the event has ended reaches its own event's scope.
    private ConditionalWeakTable<LiveRenderContext, EventLife>? _byRender;

    // The events that have ended with work still running: what the session's end disposes.
    private HashSet<EventLife>? _ended;
    private bool _disposed;

    /// <summary>How long a hook whose component has left the page keeps its event's services. Settable for tests.</summary>
    internal TimeSpan TeardownGrace { get; set; } = DefaultTeardownGrace;

    /// <summary>The scope the event in progress has opened, or null when it has resolved nothing of the app's.</summary>
    internal IServiceProvider? Opened
    {
        get
        {
            lock (_gate)
            {
                return _current?.Scope?.ServiceProvider;
            }
        }
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
        {
            // A hook that takes the provider itself is about to hand it to work of its own: that work keeps the event.
            return SynchronizationContext.Current is LifecycleSyncContext hook ? Lease(hook) : this;
        }

        return catalog.OwnsPerEvent(serviceType)
            ? Open().GetService(serviceType)
            : session.Services.GetService(serviceType);
    }

    /// <summary>A service of <paramref name="life" />'s event, whichever event is in progress now.</summary>
    internal object? GetService(EventLife life, Type serviceType) =>
        catalog.OwnsPerEvent(serviceType)
            ? Open(life).GetService(serviceType)
            : session.Services.GetService(serviceType);

    public object? GetKeyedService(Type serviceType, object? serviceKey) =>
        (session.Services as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        session.Services.GetRequiredKeyedService(serviceType, serviceKey);

    /// <summary>
    ///     Keeps the event in progress — its scope, open or still to be opened — until <paramref name="work" />, a hook
    ///     <paramref name="render" /> started for <paramref name="owner" />, is done.
    /// </summary>
    internal void Hold(Task work, Component owner, LiveRenderContext render)
    {
        EventHold hold;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var life = Current();
            life.Holds++;
            (_byRender ??= new ConditionalWeakTable<LiveRenderContext, EventLife>()).AddOrUpdate(render, life);
            hold = new EventHold(this, life);
        }

        hold.OrAfter(TeardownGrace, owner.CallbackLifetime);
        hold.Until(work);
    }

    /// <summary>
    ///     Ends the event in progress: its scope is disposed now, or once the hooks it started and
    ///     <paramref name="stillReading" /> — the reads its page went out without — are done.
    /// </summary>
    internal void Close(Task? stillReading)
    {
        if (!_open)
        {
            return;
        }

        EventLife life;
        AsyncServiceScope? done = null;
        lock (_gate)
        {
            if (_current is not { } current)
            {
                return;
            }

            life = current;
            _current = null;
            _open = false;
            life.Ended = true;
            if (stillReading is { IsCompleted: false })
            {
                life.Holds++;
            }
            else
            {
                stillReading = null;
            }

            if (life.Done)
            {
                done = life.Scope;
                life.Scope = null;
            }
            else
            {
                (_ended ??= []).Add(life);
            }
        }

        if (done is { } scope)
        {
            DisposeLater(scope);
        }

        if (stillReading is not null)
        {
            new EventHold(this, life).Until(stillReading);
        }
    }

    /// <summary>Lets go of one hold on <paramref name="life" />: its scope goes when it was the last of an ended event.</summary>
    internal void Release(EventLife life)
    {
        AsyncServiceScope? done = null;
        lock (_gate)
        {
            life.Holds--;
            if (life.Done)
            {
                done = life.Scope;
                life.Scope = null;
                _ended?.Remove(life);
            }
        }

        if (done is { } scope)
        {
            DisposeLater(scope);
        }
    }

    /// <summary>The session has ended: every event's scope is disposed, whatever is still running.</summary>
    internal void EndAll()
    {
        List<AsyncServiceScope> open = [];
        lock (_gate)
        {
            _disposed = true;
            _open = false;
            Take(_current, open);
            _current = null;
            foreach (var life in _ended ?? [])
            {
                Take(life, open);
            }

            _ended = null;
            _byRender = null;
        }

        foreach (var scope in open)
        {
            DisposeLater(scope);
        }
    }

    private static void Take(EventLife? life, List<AsyncServiceScope> into)
    {
        if (life?.Scope is { } scope)
        {
            into.Add(scope);
            life.Scope = null;
        }
    }

    // Under the gate. The event the asking flow belongs to: the one a hook that is still running was started in,
    // else the one in progress.
    private EventLife Life()
    {
        if (_byRender is not null
            && LiveRenderContext.Current is { } render
            && _byRender.TryGetValue(render, out var started)
            && !started.Done)
        {
            return started;
        }

        return Current();
    }

    // Under the gate.
    private EventLife Current()
    {
        _open = true;
        return _current ??= new EventLife();
    }

    // The provider handed to a hook that asked for it: held until the work it detached comes back, its component
    // has left the page, or the session ends — whichever is first.
    private IServiceProvider Lease(LifecycleSyncContext hook)
    {
        EventHold hold;
        EventLife life;
        lock (_gate)
        {
            if (_disposed)
            {
                return this;
            }

            life = Life();
            life.Holds++;
            hold = new EventHold(this, life);
        }

        hold.OrAfter(TeardownGrace, hook.Component.CallbackLifetime);
        HookLeases.Add(hook, hold);
        return new EventServicesLease(this, life);
    }

    private IServiceProvider Open()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return OpenIn(Life());
        }
    }

    private IServiceProvider Open(EventLife life)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return OpenIn(life);
        }
    }

    // Under the gate.
    private IServiceProvider OpenIn(EventLife life)
    {
        if (life.Scope is { } open)
        {
            return open.ServiceProvider;
        }

        var scope = scopes.CreateAsyncScope();
        try
        {
            session.SeedEventScope(scope.ServiceProvider);
        }
        catch
        {
            DisposeLater(scope);
            throw;
        }

        life.Scope = scope;
        return scope.ServiceProvider;
    }

    // Never on the caller's thread: a DbContext waits, as it is disposed, for a query still running on it — a read a
    // hook detached — and the caller is a response going out or a dispatch holding the session.
    private static void DisposeLater(AsyncServiceScope scope) =>
        _ = Task.Run(() => DisposeAsync(scope), CancellationToken.None);

    private static async Task DisposeAsync(AsyncServiceScope scope)
    {
        try
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A service of the app's that throws as it is disposed must not take the session with it: reported.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.Live", "Rask Live: disposing an event's services threw", ex);
        }
    }
}
