using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;
using Rask.Core.Forms;

namespace Rask.Core.Live;

/// <summary>
///     The services of the live event in progress: the app's scoped services from a scope of the event's own — one for
///     each component it runs code for — opened on first use, and everything else from the session.
/// </summary>
/// <remarks>
///     <para>
///         One per session, so making it ambient allocates nothing: an event that resolves nothing of the app's and
///         leaves nothing running opens no scope and allocates nothing.
///     </para>
///     <para>
///         As a Livewire component's action is a request of its own, each component's part of an event has its own
///         scope (<see cref="EventLives" />): the hooks of three components loading at once never share a
///         <c>DbContext</c>. Within one component the event's instance is one — its handler, its hooks, what they call.
///         What no component runs (a render, a read it started) gets the event's own.
///     </para>
///     <para>
///         A part's scope lives as long as the work it started: every lifecycle hook of that component the event's
///         renders started and every read it went out without. A hook goes on long after a navigation has been sent —
///         what it resolves after an await is still its own part's, found again through its context, and is disposed
///         when the last of that work is done. A hook whose component has left the page keeps it for
///         <see cref="TeardownGrace" /> more at most; the session's end disposes every one.
///     </para>
/// </remarks>
internal sealed class EventServices(LiveSessionBase session, EventScopeCatalog catalog, IServiceScopeFactory scopes)
    : IKeyedServiceProvider
{
    /// <summary>How long a hook whose component has left the page keeps its event's services.</summary>
    internal static readonly TimeSpan DefaultTeardownGrace = TimeSpan.FromSeconds(10);

    // The component's part a flow resolved through, carried to its continuations — one that left the hook's context
    // (ConfigureAwait(false), Task.Run) included. Set on the first resolution only: a flow that resolves nothing pays nothing.
    private static readonly AsyncLocal<EventLife?> CarriedLife = new();

    private readonly Lock _gate = new();

    // The event in progress, or null while it has resolved nothing and started nothing that runs on.
    private EventLives? _current;

    // Whether there is an event in progress to end, read without the gate by the end of every event: every event's path.
    private volatile bool _open;

    // The component whose handler the session is dispatching, while it is: what that handler's code resolves through.
    private volatile Component? _dispatching;

    // The render that started a hook still running, to the event it belongs to: what a flow that names no component
    // reaches after the event has ended.
    private ConditionalWeakTable<LiveRenderContext, EventLives>? _byRender;

    // A hook's context, to its component's part of the event that started it: what its continuations resolve through.
    private ConditionalWeakTable<LifecycleSyncContext, EventLife>? _byHook;

    // The parts of ended events with work still running: what the session's end disposes.
    private HashSet<EventLife>? _ended;
    private bool _disposed;

    /// <summary>How long a hook whose component has left the page keeps its event's services. Settable for tests.</summary>
    internal TimeSpan TeardownGrace { get; set; } = DefaultTeardownGrace;

    /// <summary>The first scope the event in progress has opened, or null when it has resolved nothing of the app's.</summary>
    internal IServiceProvider? Opened
    {
        get
        {
            lock (_gate)
            {
                return _current?.Opened;
            }
        }
    }

    /// <summary>The part the asking flow carries — read before a hook runs, to put back after (<see cref="Restore" />).</summary>
    internal static EventLife? Carried => CarriedLife.Value;

    /// <summary>
    ///     Puts back what the flow carried before a hook ran: a hook that is not <c>async</c> and resolved something would
    ///     otherwise leave its part on the render walk. Writes only when it changed.
    /// </summary>
    internal static void Restore(EventLife? before)
    {
        if (CarriedLife.Value != before)
        {
            CarriedLife.Value = before;
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

    /// <summary>A service of <paramref name="life" />'s part of its event, whichever event is in progress now.</summary>
    internal object? GetService(EventLife life, Type serviceType) =>
        catalog.OwnsPerEvent(serviceType)
            ? Open(life).GetService(serviceType)
            : session.Services.GetService(serviceType);

    public object? GetKeyedService(Type serviceType, object? serviceKey) =>
        (session.Services as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        session.Services.GetRequiredKeyedService(serviceType, serviceKey);

    /// <summary>
    ///     Marks <paramref name="owner" />'s handler as running until the returned scope is disposed: what it resolves
    ///     is its component's part of the event. Nothing is written for a provider that is not a session's.
    /// </summary>
    internal static DispatchOwner Dispatching(IServiceProvider? services, Component owner)
    {
        if (services is not EventServices events)
        {
            return default;
        }

        var previous = events._dispatching;
        events._dispatching = owner;
        return new DispatchOwner(events, previous);
    }

    /// <summary>
    ///     Keeps <paramref name="owner" />'s part of the event in progress — its scope, open or still to be opened —
    ///     until <paramref name="work" />, a hook <paramref name="render" /> started under <paramref name="hook" />, is done.
    /// </summary>
    internal void Hold(Task work, Component owner, LiveRenderContext render, LifecycleSyncContext hook)
    {
        EventHold hold;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var life = OfHook(hook);
            life.Holds++;
            (_byRender ??= new ConditionalWeakTable<LiveRenderContext, EventLives>()).AddOrUpdate(render, Current());
            hold = new EventHold(this, life);
        }

        hold.OrAfter(TeardownGrace, owner.CallbackLifetime);
        hold.Until(work);
    }

    /// <summary>
    ///     Ends the event in progress: each part's scope is disposed now, or once its own hooks are done — the event's
    ///     own once <paramref name="stillReading" />, the reads its page went out without, are back too.
    /// </summary>
    internal void Close(Task? stillReading)
    {
        if (!_open)
        {
            return;
        }

        EventLife? reading = null;
        List<AsyncServiceScope> done = [];
        lock (_gate)
        {
            if (_current is not { } lives)
            {
                return;
            }

            _current = null;
            _open = false;
            if (stillReading is { IsCompleted: false })
            {
                reading = lives.Root;
                reading.Holds++;
            }

            lives.End(done, _ended ??= []);
        }

        foreach (var scope in done)
        {
            DisposeLater(scope);
        }

        if (reading is not null)
        {
            new EventHold(this, reading).Until(stillReading!);
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
            _current?.TakeAll(open);
            _current = null;
            foreach (var life in _ended ?? [])
            {
                Take(life, open);
            }

            _ended = null;
            _byRender = null;
            _byHook = null;
        }

        foreach (var scope in open)
        {
            DisposeLater(scope);
        }
    }

    /// <summary>Moves <paramref name="life" />'s scope, if it has one, into <paramref name="into" />.</summary>
    internal static void Take(EventLife? life, List<AsyncServiceScope> into)
    {
        if (life?.Scope is { } scope)
        {
            into.Add(scope);
            life.Scope = null;
        }
    }

    // Under the gate. The part the asking flow belongs to: the hook's whose context it runs in, the part it carries,
    // the handler's being dispatched, the event's whose render started it — else the event in progress's own.
    private EventLife Life()
    {
        if (SynchronizationContext.Current is LifecycleSyncContext hook)
        {
            return OfHook(hook);
        }

        if (CarriedLife.Value is { } carried && carried.Services == this && !carried.Done)
        {
            return carried;
        }

        if (_dispatching is { } owner && ReferenceEquals(DispatchServicesScope.Current, this))
        {
            return Current().For(owner);
        }

        if (_byRender is not null
            && LiveRenderContext.Current is { } render
            && _byRender.TryGetValue(render, out var started))
        {
            if (!started.Ended)
            {
                return started.Root;
            }

            if (started.RootIfAny is { Done: false } root)
            {
                return root;
            }
        }

        return Current().Root;
    }

    // Under the gate. The part of the event a hook's component has in the event that started the hook — or, for a hook
    // the session never saw start, or whose part is done, in the event in progress.
    private EventLife OfHook(LifecycleSyncContext hook)
    {
        _byHook ??= new ConditionalWeakTable<LifecycleSyncContext, EventLife>();
        if (_byHook.TryGetValue(hook, out var life) && !life.Done)
        {
            return life;
        }

        life = Current().For(hook.Component);
        _byHook.AddOrUpdate(hook, life);
        return life;
    }

    // Under the gate.
    private EventLives Current()
    {
        _open = true;
        return _current ??= new EventLives(this);
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

            life = OfHook(hook);
            life.Holds++;
            hold = new EventHold(this, life);
        }

        hold.OrAfter(TeardownGrace, hook.Component.CallbackLifetime);
        HookLeases.Add(hook, hold);
        return new EventServicesLease(this, life);
    }

    private IServiceProvider Open()
    {
        EventLife life;
        IServiceProvider services;
        bool component;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            life = Life();
            services = OpenIn(life);
            component = !ReferenceEquals(life, _current?.RootIfAny);
        }

        // A component's part goes on with the flow — past a ConfigureAwait(false), into a Task.Run — once it is asked for.
        if (component && CarriedLife.Value != life)
        {
            CarriedLife.Value = life;
        }

        return services;
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

    /// <summary>The handler being dispatched, until disposed: puts back the one before it.</summary>
    internal readonly struct DispatchOwner(EventServices? services, Component? previous) : IDisposable
    {
        public void Dispose()
        {
            if (services is not null)
            {
                services._dispatching = previous;
            }
        }
    }
}
