using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Live;

/// <summary>
///     The few scoped services that belong to a live session rather than to one of its events: its route, its
///     connection, its user, its toaster, its query cache. Everything else scoped is made fresh for each event.
/// </summary>
/// <remarks>
///     A registration marked here is answered, inside an event's scope, with the session's own instance — so a
///     service of the app's made for the event that takes <c>IUserProvider</c> or <c>IToaster</c> gets the page's,
///     not a new one nobody shows. The session's scope itself builds them as registered.
/// </remarks>
internal static class SessionScoped
{
    /// <summary>Makes every scoped registration added since <paramref name="from" /> the session's.</summary>
    /// <param name="services">The app's services.</param>
    /// <param name="from">The count of <paramref name="services" /> before the host's own registrations.</param>
    /// <param name="forwards">How a type whose instance is disposable is handed to an event: a view that is not.</param>
    internal static void Since(
        IServiceCollection services, int from, IReadOnlyDictionary<Type, Func<object, object>>? forwards = null)
    {
        for (var i = from; i < services.Count; i++)
        {
            if (services[i] is { Lifetime: ServiceLifetime.Scoped, IsKeyedService: false } descriptor && !Is(descriptor))
            {
                services[i] = Bound(descriptor, forwards?.GetValueOrDefault(descriptor.ServiceType));
            }
        }

        services.TryAddEventScopeLink();
    }

    /// <summary>Makes the scoped registration of <paramref name="serviceType" /> the session's.</summary>
    internal static void Mark(IServiceCollection services, Type serviceType, Func<object, object>? forward = null)
    {
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i] is { Lifetime: ServiceLifetime.Scoped, IsKeyedService: false } descriptor
                && descriptor.ServiceType == serviceType
                && !Is(descriptor))
            {
                services[i] = Bound(descriptor, forward);
            }
        }

        services.TryAddEventScopeLink();
    }

    /// <summary>Whether <paramref name="descriptor" /> is one of the session's.</summary>
    internal static bool Is(ServiceDescriptor descriptor) =>
        !descriptor.IsKeyedService && descriptor.ImplementationFactory?.Target is Factory;

    private static void TryAddEventScopeLink(this IServiceCollection services)
    {
        if (!services.Any(static d => d.ServiceType == typeof(EventScopeLink)))
        {
            services.AddScoped<EventScopeLink>();
        }
    }

    private static ServiceDescriptor Bound(ServiceDescriptor descriptor, Func<object, object>? forward) =>
        ServiceDescriptor.Scoped(descriptor.ServiceType, new Factory(descriptor, forward).Create);

    private sealed class Factory
    {
        private readonly Type _serviceType;
        private readonly Func<IServiceProvider, object>? _factory;
        private readonly Func<object, object>? _forward;

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        private readonly Type? _implementation;

        internal Factory(ServiceDescriptor descriptor, Func<object, object>? forward)
        {
            _serviceType = descriptor.ServiceType;
            _factory = descriptor.ImplementationFactory;
            _implementation = descriptor.ImplementationType;
            _forward = forward;
        }

        internal object Create(IServiceProvider services)
        {
            if (services.GetService<EventScopeLink>()?.Session is not { } session)
            {
                return _factory is { } factory
                    ? factory(services)
                    : ActivatorUtilities.CreateInstance(services, _implementation!);
            }

            var own = session.GetRequiredService(_serviceType);
            if (_forward is { } forward)
            {
                return forward(own);
            }

            // The event's scope disposes what it hands out: the session's instance would go with the event.
            return own is IDisposable or IAsyncDisposable
                ? throw new InvalidOperationException(
                    $"{_serviceType} is the live session's and disposable, so an event's scope cannot hand it out: "
                    + "give it a forward that is not disposable (SessionScoped).")
                : own;
        }
    }
}
