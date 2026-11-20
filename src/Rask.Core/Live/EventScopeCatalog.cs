using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Live;

/// <summary>
///     Which services a live event gets fresh, in a scope of its own: every scoped and transient registration except
///     the few that are the session's (<see cref="SessionScoped" />).
/// </summary>
/// <remarks>
///     What a request would have had one of, an event has one of: the app's <c>DbContext</c>, its repositories, the
///     batteries' scoped services that read the user or the tenant. A transient is made where it is asked for, so in
///     the event's scope too — which is how the dispatcher an event reaches hands its handlers the event's context.
///     A singleton is the same instance from either side, and is answered by the session without opening a scope.
/// </remarks>
internal sealed class EventScopeCatalog(IServiceCollection services)
{
    private HashSet<Type>? _owned;
    private HashSet<(Type Type, object? Key)>? _ownedKeyed;

    /// <summary>Registers the catalog of <paramref name="services" />, once.</summary>
    internal static void Add(IServiceCollection services)
    {
        if (!services.Any(static d => d.ServiceType == typeof(EventScopeCatalog)))
        {
            services.AddSingleton(new EventScopeCatalog(services));
        }
    }

    /// <summary>
    ///     Keeps <paramref name="type" />, a scoped service of an assembly that cannot mark its own registration
    ///     (<see cref="SessionScoped" />), the session's when an event asks for it.
    /// </summary>
    internal static void KeepForSession(IServiceCollection services, Type type) =>
        services.AddSingleton(new SessionKept(type));

    /// <summary>Whether <paramref name="type" /> is made in the event's scope rather than taken from the session.</summary>
    internal bool OwnsPerEvent(Type type)
    {
        var owned = _owned ?? Build();
        if (owned.Contains(type))
        {
            return true;
        }

        if (!type.IsConstructedGenericType)
        {
            return false;
        }

        // IEnumerable<T> is answered as T is; a closed generic as its open registration is.
        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IEnumerable<>)
            ? OwnsPerEvent(type.GenericTypeArguments[0])
            : owned.Contains(definition);
    }

    /// <summary>
    ///     Whether the keyed <paramref name="type" /> under <paramref name="key" /> is made in the event's scope: a keyed
    ///     <c>DbContext</c> or repository is the event's exactly as an unkeyed one is, not one instance for the session.
    /// </summary>
    internal bool OwnsPerEvent(Type type, object? key)
    {
        if (_ownedKeyed is null)
        {
            Build();
        }

        var owned = _ownedKeyed!;
        if (owned.Contains((type, key)) || owned.Contains((type, KeyedService.AnyKey)))
        {
            return true;
        }

        if (!type.IsConstructedGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IEnumerable<>)
            ? OwnsPerEvent(type.GenericTypeArguments[0], key)
            : owned.Contains((definition, key)) || owned.Contains((definition, KeyedService.AnyKey));
    }

    // Read on first use, after the container is built: what the app registered after the host is in it too.
    private HashSet<Type> Build()
    {
        var kept = services.Select(static d => d.ImplementationInstance).OfType<SessionKept>().Select(static k => k.Type).ToHashSet();
        var owned = new HashSet<Type>();
        var ownedKeyed = new HashSet<(Type, object?)>();
        foreach (var descriptor in services)
        {
            if (kept.Contains(descriptor.ServiceType) || descriptor.Lifetime == ServiceLifetime.Singleton || SessionScoped.Is(descriptor))
            {
                continue;
            }

            if (descriptor.IsKeyedService)
            {
                ownedKeyed.Add((descriptor.ServiceType, descriptor.ServiceKey));
            }
            else
            {
                owned.Add(descriptor.ServiceType);
            }
        }

        _ownedKeyed = ownedKeyed;
        _owned = owned;
        return owned;
    }
}
