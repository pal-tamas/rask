using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

/// <summary>
/// The reflection-free dispatch table. Populated at module load by the code the Rask.Cqrs source
/// generator emits (a <c>[ModuleInitializer]</c> per assembly that contains handlers), then read by
/// <see cref="LocalDispatcher"/> at dispatch time and by <c>AddRaskCqrs</c> at registration time. Every
/// entry is a compile-time closed-generic delegate, so no runtime reflection or assembly scanning
/// occurs. This type is public only so generated code can call into it; you do not use it directly.
/// </summary>
public static class CqrsRegistry
{
    /// <summary>Invokes the handler pipeline for a query or command. Returns the handler's task
    /// (<c>Task&lt;TResult&gt;</c> for queries/result-commands, <c>Task&lt;Unit&gt;</c> for void commands).</summary>
    public delegate Task RequestInvoker(IServiceProvider provider, object request, CancellationToken cancellationToken);

    /// <summary>Invokes every handler for an event.</summary>
    public delegate Task EventInvoker(IServiceProvider provider, object e, CancellationToken cancellationToken);

    private static readonly Lock _lock = new();
    private static readonly Dictionary<Type, RequestInvoker> _manualRequests = new();
    private static readonly Dictionary<Type, EventInvoker> _manualEvents = new();

    // One entry per contributing assembly, keyed by that assembly's generated registry type.
    private static readonly List<(object Key, (Type Type, RequestInvoker Invoker)[] Items)> _requestGroups = new();
    private static readonly List<(object Key, (Type Type, EventInvoker Invoker)[] Items)> _eventGroups = new();

    // The flattened dispatch tables. Rebuilt under the lock and installed in a single store, so a
    // dispatch in flight observes either the complete old table or the complete new one.
    private static volatile Dictionary<Type, RequestInvoker> _requests =
        new Dictionary<Type, RequestInvoker>();

    private static volatile Dictionary<Type, EventInvoker> _events =
        new Dictionary<Type, EventInvoker>();

    private static readonly List<(object Key, (Type Type, SubscriptionRegistration Registration)[] Items)> _subscriptionGroups = new();

    private static volatile Dictionary<Type, SubscriptionRegistration> _subscriptions =
        new Dictionary<Type, SubscriptionRegistration>();

    // Durable handlers, per contributing assembly: (event type, (handler type, invoker)).
    private static readonly List<(object Key, (Type Type, (Type Handler, EventInvoker Invoker) Invoker)[] Items)> _durableGroups = new();

    private static volatile DurableTable _durable = DurableTable.Empty;

    // What each handler's [Authorize] asks, per contributing assembly and keyed by request type.
    private static readonly List<(object Key, (Type Type, RequestAuthorization Declared)[] Items)> _authorizationGroups = new();

    private static volatile Dictionary<Type, RequestAuthorization> _authorization =
        new Dictionary<Type, RequestAuthorization>();

    // The modules whose initializer has been forced, so a lookup that misses does it at most once per module.
    private static readonly ConcurrentDictionary<System.Reflection.Module, bool> _initialized = new();

    private static readonly ConcurrentQueue<Action<IServiceCollection, ServiceLifetime>> Registrations = new();

    /// <summary>Maps a query/command type to its dispatch invoker.</summary>
    public static void RegisterRequest(Type requestType, RequestInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(invoker);
        lock (_lock)
        {
            _manualRequests[requestType] = invoker;
            RebuildRequests();
        }
    }

    /// <summary>Maps an event type to its fan-out invoker.</summary>
    public static void RegisterEvent(Type eventType, EventInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(invoker);
        lock (_lock)
        {
            _manualEvents[eventType] = invoker;
            RebuildEvents();
        }
    }

    /// <summary>
    ///     Installs <paramref name="registrations" /> as the complete set of request invokers owned by
    ///     <paramref name="groupKey" />. Generated per-assembly initializers call this with their own
    ///     <c>typeof(__RaskCqrsRegistry)</c>, so re-running one under hot reload swaps that assembly's
    ///     dispatch table rather than merging into it — deleting the last handler for a request now stops
    ///     dispatching it, instead of silently keeping the invoker built from the old IL.
    /// </summary>
    public static void ReplaceRequests(object groupKey, IEnumerable<(Type Type, RequestInvoker Invoker)> registrations)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(registrations);

        var items = registrations as (Type Type, RequestInvoker Invoker)[] ?? registrations.ToArray();
        lock (_lock)
        {
            if (ReplaceGroup(_requestGroups, groupKey, items))
            {
                RebuildRequests();
            }
        }
    }

    /// <summary>
    ///     The event counterpart of <see cref="ReplaceRequests" />.
    /// </summary>
    public static void ReplaceEvents(
        object groupKey,
        IEnumerable<(Type Type, EventInvoker Invoker)> registrations)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(registrations);

        var items = registrations as (Type Type, EventInvoker Invoker)[] ?? registrations.ToArray();
        lock (_lock)
        {
            if (ReplaceGroup(_eventGroups, groupKey, items))
            {
                RebuildEvents();
            }
        }
    }

    /// <summary>
    ///     Installs <paramref name="registrations" /> as the complete set of <see cref="IDurableHandler{TEvent}" />s
    ///     owned by <paramref name="groupKey" />: each event type, the handler, and the invoker that runs that one
    ///     handler. The same per-assembly swap as <see cref="ReplaceRequests" />.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void ReplaceDurableHandlers(
        object groupKey,
        IEnumerable<(Type EventType, Type HandlerType, EventInvoker Invoker)> registrations)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(registrations);

        var items = registrations.Select(static r => (r.EventType, (r.HandlerType, r.Invoker))).ToArray();
        lock (_lock)
        {
            if (ReplaceGroup(_durableGroups, groupKey, items))
            {
                _durable = DurableTable.From(_durableGroups.SelectMany(static g => g.Items));
            }
        }
    }

    /// <summary>
    ///     The names of the durable handlers of <paramref name="eventType" /> — what the outbox stores one row per —
    ///     or an empty list when it has none.
    /// </summary>
    /// <param name="eventType">The event's concrete type.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IReadOnlyList<string> DurableHandlersOf(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return _durable.ByEvent.TryGetValue(eventType, out var names) ? names : [];
    }

    /// <summary>Whether any loaded assembly declares an <see cref="IDurableHandler{TEvent}" /> — so the outbox is in use.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool HasDurableHandlers => _durable.ByHandler.Count > 0;

    /// <summary>The invoker that runs the durable handler stored as <paramref name="handler" />, or null.</summary>
    /// <param name="handler">A name from <see cref="DurableHandlersOf" />.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static EventInvoker? FindDurableHandler(string handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _durable.ByHandler.TryGetValue(handler, out var invoker) ? invoker : null;
    }

    /// <summary>The event type stored as <paramref name="name" /> by <see cref="NameOf" />, when a durable handler takes it.</summary>
    /// <param name="name">The stored type name.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Type? FindDurableEvent(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _durable.EventsByName.TryGetValue(name, out var type) ? type : null;
    }

    /// <summary>
    ///     The name a type is stored under: its full name, dot-separated even for a nested type (which is what Roslyn
    ///     writes), so a stored row and a generated registration agree.
    /// </summary>
    /// <param name="type">The event or handler type.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string NameOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return (type.FullName ?? type.Name).Replace('+', '.');
    }

    /// <summary>
    ///     Installs <paramref name="registrations" /> as the complete set of <see cref="ISubscription{TEvent}" />
    ///     records owned by <paramref name="groupKey" />, the same per-assembly swap as <see cref="ReplaceRequests" />.
    /// </summary>
    public static void ReplaceSubscriptions(
        object groupKey,
        IEnumerable<(Type Type, SubscriptionRegistration Registration)> registrations)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(registrations);

        var items = registrations as (Type Type, SubscriptionRegistration Registration)[] ?? registrations.ToArray();
        lock (_lock)
        {
            if (!ReplaceGroup(_subscriptionGroups, groupKey, items))
            {
                return;
            }

            var map = new Dictionary<Type, SubscriptionRegistration>();
            foreach (var (_, group) in _subscriptionGroups)
            {
                foreach (var (type, registration) in group)
                {
                    map[type] = registration;
                }
            }

            _subscriptions = map;
        }
    }

    /// <summary>
    ///     Installs <paramref name="registrations" /> as what the handlers owned by <paramref name="groupKey" />
    ///     declared with <c>[Authorize]</c>, per request type — the same per-assembly swap as
    ///     <see cref="ReplaceRequests" />. Local dispatch reads it, so the declaration holds for a request that
    ///     never crosses the wire.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void ReplaceAuthorization(
        object groupKey,
        IEnumerable<(Type Type, RequestAuthorization Declared)> registrations)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(registrations);

        var items = registrations as (Type Type, RequestAuthorization Declared)[] ?? registrations.ToArray();
        lock (_lock)
        {
            ReplaceGroup(_authorizationGroups, groupKey, items);

            var map = new Dictionary<Type, RequestAuthorization>();
            foreach (var (type, declared) in _authorizationGroups.SelectMany(static g => g.Items))
            {
                map[type] = declared;
            }

            _authorization = map;
        }
    }

    /// <summary>What the handler of <paramref name="requestType" /> declared with <c>[Authorize]</c>, or null.</summary>
    internal static RequestAuthorization? FindAuthorization(Type requestType) =>
        _authorization.TryGetValue(requestType, out var declared) ? declared : null;

    /// <summary>
    ///     Asks the <see cref="IWatchPolicy{TSubscription}" /> registered in <paramref name="provider" /> whether its
    ///     subscriber may open <paramref name="subscription" />. No policy is a refusal. Called by generated code.
    /// </summary>
    /// <typeparam name="TSubscription">The subscription record being opened.</typeparam>
    /// <param name="provider">The subscriber's scope.</param>
    /// <param name="subscription">What is being asked for.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    public static Task<bool> CanWatch<TSubscription>(
        IServiceProvider provider,
        TSubscription subscription,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetService<IWatchPolicy<TSubscription>>() is { } policy
            ? policy.CanWatch(subscription, cancellationToken)
            : Task.FromResult(false);
    }

    /// <summary>What the generator recorded about a subscription record, or null when it declared none.</summary>
    /// <remarks>
    ///     The table is filled by the declaring assembly's module initializer, which the runtime runs on first access to
    ///     a member of that assembly — and a caller that has only named the type has not accessed one. So the module
    ///     constructor is forced once per module, behind a flag, rather than on every miss.
    /// </remarks>
    internal static SubscriptionRegistration? FindSubscription(Type subscriptionType)
    {
        if (_subscriptions.TryGetValue(subscriptionType, out var registration))
        {
            return registration;
        }

        if (!_initialized.TryAdd(subscriptionType.Module, true))
        {
            return null;
        }

        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(subscriptionType.Module.ModuleHandle);
        return _subscriptions.TryGetValue(subscriptionType, out registration) ? registration : null;
    }

    // Caller holds _lock. Returns false when the group's contribution is unchanged, so an unrelated hot
    // reload — which re-runs every RefreshAll() — does not rebuild a table nothing changed in. The
    // invokers are static lambdas, so a regenerated set compares equal when the handlers are unchanged.
    private static bool ReplaceGroup<TInvoker>(
        List<(object Key, (Type Type, TInvoker Invoker)[] Items)> groups,
        object groupKey,
        (Type Type, TInvoker Invoker)[] items)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            if (!ReferenceEquals(groups[i].Key, groupKey))
            {
                continue;
            }

            if (groups[i].Items.AsSpan().SequenceEqual(items))
            {
                return false;
            }

            groups[i] = (groupKey, items);
            return true;
        }

        groups.Add((groupKey, items));
        return true;
    }

    // Caller holds _lock. Manual registrations are applied last so an explicit one is never clobbered.
    private static void RebuildRequests()
    {
        var map = new Dictionary<Type, RequestInvoker>();
        foreach (var (_, items) in _requestGroups)
        {
            foreach (var (type, invoker) in items)
            {
                map[type] = invoker;
            }
        }

        foreach (var (type, invoker) in _manualRequests)
        {
            map[type] = invoker;
        }

        _requests = map;
    }

    // Caller holds _lock.
    private static void RebuildEvents()
    {
        var map = new Dictionary<Type, EventInvoker>();
        foreach (var (_, items) in _eventGroups)
        {
            foreach (var (type, invoker) in items)
            {
                map[type] = invoker;
            }
        }

        foreach (var (type, invoker) in _manualEvents)
        {
            map[type] = invoker;
        }

        _events = map;
    }

    /// <summary>Called by generated code to enqueue a handler's DI registration (applied by <c>AddRaskCqrs</c>).</summary>
    public static void RegisterServices(Action<IServiceCollection, ServiceLifetime> registration) =>
        Registrations.Enqueue(registration);

    internal static RequestInvoker GetRequestInvoker(Type requestType) =>
        _requests.TryGetValue(requestType, out var invoker)
            ? invoker
            : throw new InvalidOperationException(
                $"No handler is registered for '{requestType}'. Ensure a handler implementing " +
                "IQueryHandler/ICommandHandler for it exists, that its assembly is loaded, and that " +
                "AddRaskCqrs() was called during startup.");

    /// <summary>
    ///     Finds the fan-out invoker for an event type, or null when nothing handles it here.
    /// </summary>
    /// <param name="eventType">The event's concrete type.</param>
    /// <remarks>
    ///     Public so a remote transport can <em>compose</em> with the local fan-out rather than replace
    ///     it: on a client, publishing an event should still reach the handlers in that process —
    ///     a badge, a toast — and also travel to the server. Replacing the invoker outright would
    ///     silently drop the local ones.
    /// </remarks>
    public static EventInvoker? FindEventInvoker(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return _events.TryGetValue(eventType, out var invoker) ? invoker : null;
    }

    internal static EventInvoker? GetEventInvoker(Type eventType) =>
        _events.TryGetValue(eventType, out var invoker) ? invoker : null;

    internal static void ApplyRegistrations(IServiceCollection services, ServiceLifetime lifetime)
    {
        foreach (var registration in Registrations)
        {
            registration(services, lifetime);
        }
    }

    // The flattened durable lookups, built together and installed in one store.
    private sealed record DurableTable(
        Dictionary<Type, string[]> ByEvent,
        Dictionary<string, EventInvoker> ByHandler,
        Dictionary<string, Type> EventsByName)
    {
        public static readonly DurableTable Empty = new([], new(StringComparer.Ordinal), new(StringComparer.Ordinal));

        public static DurableTable From(IEnumerable<(Type Type, (Type Handler, EventInvoker Invoker) Invoker)> items)
        {
            var byEvent = new Dictionary<Type, List<string>>();
            var byHandler = new Dictionary<string, EventInvoker>(StringComparer.Ordinal);
            var eventsByName = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var (eventType, (handlerType, invoker)) in items)
            {
                // Keyed by handler AND event: one class may handle two events durably, and each is its own row.
                var name = $"{NameOf(handlerType)}:{NameOf(eventType)}";
                if (!byEvent.TryGetValue(eventType, out var names))
                {
                    byEvent[eventType] = names = [];
                }

                names.Add(name);
                byHandler[name] = invoker;
                eventsByName[NameOf(eventType)] = eventType;
            }

            return new DurableTable(byEvent.ToDictionary(static p => p.Key, static p => p.Value.ToArray()), byHandler, eventsByName);
        }
    }
}
