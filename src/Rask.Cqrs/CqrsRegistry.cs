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

    private static readonly ConcurrentDictionary<Type, RequestInvoker> Requests = new();
    private static readonly ConcurrentDictionary<Type, NotificationInvoker> Notifications = new();
    private static readonly ConcurrentQueue<Action<IServiceCollection, ServiceLifetime>> Registrations = new();

    /// <summary>Called by generated code to map a query/command type to its dispatch invoker.</summary>
    public static void RegisterRequest(Type requestType, RequestInvoker invoker) => Requests[requestType] = invoker;

    /// <summary>Called by generated code to map a notification type to its fan-out invoker.</summary>
    public static void RegisterNotification(Type notificationType, NotificationInvoker invoker) =>
        Notifications[notificationType] = invoker;

    /// <summary>Called by generated code to enqueue a handler's DI registration (applied by <c>AddRaskCqrs</c>).</summary>
    public static void RegisterServices(Action<IServiceCollection, ServiceLifetime> registration) =>
        Registrations.Enqueue(registration);

    internal static RequestInvoker GetRequestInvoker(Type requestType) =>
        Requests.TryGetValue(requestType, out var invoker)
            ? invoker
            : throw new InvalidOperationException(
                $"No handler is registered for '{requestType}'. Ensure a handler implementing " +
                "IQueryHandler/ICommandHandler for it exists, that its assembly is loaded, and that " +
                "AddRaskCqrs() was called during startup.");

    internal static NotificationInvoker? GetNotificationInvoker(Type notificationType) =>
        Notifications.TryGetValue(notificationType, out var invoker) ? invoker : null;

    internal static void ApplyRegistrations(IServiceCollection services, ServiceLifetime lifetime)
    {
        foreach (var registration in Registrations)
        {
            registration(services, lifetime);
        }
    }
}
