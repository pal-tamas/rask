using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

/// <summary>
/// The reflection-free dispatch table. Populated at module load by the code the Rask.Cqrs source
/// generator emits (a <c>[ModuleInitializer]</c> per assembly that contains handlers), then read by
/// <see cref="Dispatcher"/> at dispatch time and by <c>AddRaskCqrs</c> at registration time. Every
/// entry is a compile-time closed-generic delegate, so no runtime reflection or assembly scanning
/// occurs. This type is public only so generated code can call into it; you do not use it directly.
/// </summary>
public static class CqrsRegistry
{
    /// <summary>Invokes the handler pipeline for a query or command. Returns the handler's task
    /// (<c>Task&lt;TResult&gt;</c> for queries/result-commands, <c>Task&lt;Unit&gt;</c> for void commands).</summary>
    public delegate Task RequestInvoker(IServiceProvider provider, object request, CancellationToken cancellationToken);

    /// <summary>Invokes every handler for a notification.</summary>
    public delegate Task NotificationInvoker(IServiceProvider provider, object notification, CancellationToken cancellationToken);

    private static readonly object _lock = new();
    private static readonly Dictionary<Type, RequestInvoker> _manualRequests = new();
    private static readonly Dictionary<Type, NotificationInvoker> _manualNotifications = new();

    // One entry per contributing assembly, keyed by that assembly's generated registry type.
    private static readonly List<(object Key, (Type Type, RequestInvoker Invoker)[] Items)> _requestGroups = new();
    private static readonly List<(object Key, (Type Type, NotificationInvoker Invoker)[] Items)> _notificationGroups = new();

    // The flattened dispatch tables. Rebuilt under the lock and installed in a single store, so a
    // dispatch in flight observes either the complete old table or the complete new one.
    private static volatile IReadOnlyDictionary<Type, RequestInvoker> _requests =
        new Dictionary<Type, RequestInvoker>();

    private static volatile IReadOnlyDictionary<Type, NotificationInvoker> _notifications =
        new Dictionary<Type, NotificationInvoker>();

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

    /// <summary>Maps a notification type to its fan-out invoker.</summary>
    public static void RegisterNotification(Type notificationType, NotificationInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(notificationType);
        ArgumentNullException.ThrowIfNull(invoker);
        lock (_lock)
        {
            _manualNotifications[notificationType] = invoker;
            RebuildNotifications();
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
    ///     The notification counterpart of <see cref="ReplaceRequests" />.
    /// </summary>
    public static void ReplaceNotifications(
        object groupKey,
        IEnumerable<(Type Type, NotificationInvoker Invoker)> registrations)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(registrations);

        var items = registrations as (Type Type, NotificationInvoker Invoker)[] ?? registrations.ToArray();
        lock (_lock)
        {
            if (ReplaceGroup(_notificationGroups, groupKey, items))
            {
                RebuildNotifications();
            }
        }
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
    private static void RebuildNotifications()
    {
        var map = new Dictionary<Type, NotificationInvoker>();
        foreach (var (_, items) in _notificationGroups)
        {
            foreach (var (type, invoker) in items)
            {
                map[type] = invoker;
            }
        }

        foreach (var (type, invoker) in _manualNotifications)
        {
            map[type] = invoker;
        }

        _notifications = map;
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
    ///     Finds the fan-out invoker for a notification type, or null when nothing handles it here.
    /// </summary>
    /// <param name="notificationType">The notification's concrete type.</param>
    /// <remarks>
    ///     Public so a remote transport can <em>compose</em> with the local fan-out rather than replace
    ///     it: on a client, publishing a notification should still reach the handlers in that process —
    ///     a badge, a toast — and also travel to the server. Replacing the invoker outright would
    ///     silently drop the local ones.
    /// </remarks>
    public static NotificationInvoker? FindNotificationInvoker(Type notificationType)
    {
        ArgumentNullException.ThrowIfNull(notificationType);
        return _notifications.TryGetValue(notificationType, out var invoker) ? invoker : null;
    }

    internal static NotificationInvoker? GetNotificationInvoker(Type notificationType) =>
        _notifications.TryGetValue(notificationType, out var invoker) ? invoker : null;

    internal static void ApplyRegistrations(IServiceCollection services, ServiceLifetime lifetime)
    {
        foreach (var registration in Registrations)
        {
            registration(services, lifetime);
        }
    }
}
