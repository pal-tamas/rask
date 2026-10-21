using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Live;

/// <summary>
///     Which services a live event gets fresh, in a scope of its own: the app's scoped services, and every transient.
/// </summary>
/// <remarks>
///     The framework's scoped services — the route, the toaster, the user, the browser — are the session's and stay
///     the session's. The app's are what a request would have had one of each: its <c>DbContext</c>, its repositories,
///     its validators. A transient is made where it is asked for, so it is made in the event's scope too: that is how
///     a dispatcher the event resolves hands its handlers the event's <c>DbContext</c>.
/// </remarks>
internal sealed class EventScopeCatalog(IServiceCollection services)
{
    private HashSet<Type>? _owned;

    /// <summary>Registers the catalog of <paramref name="services" />, once.</summary>
    internal static void Add(IServiceCollection services)
    {
        if (!services.Any(static d => d.ServiceType == typeof(EventScopeCatalog)))
        {
            services.AddSingleton(new EventScopeCatalog(services));
        }
    }

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

    // Read on first use, after the container is built: what the app registered after the host is in it too.
    private HashSet<Type> Build()
    {
        var owned = new HashSet<Type>();
        foreach (var descriptor in services)
        {
            if (!descriptor.IsKeyedService
                && (descriptor.Lifetime == ServiceLifetime.Transient
                    || (descriptor.Lifetime == ServiceLifetime.Scoped && !IsFramework(descriptor.ServiceType))))
            {
                owned.Add(descriptor.ServiceType);
            }
        }

        _owned = owned;
        return owned;
    }

    private static bool IsFramework(Type type)
    {
        var name = type.Assembly.GetName().Name ?? string.Empty;
        return name is "System" or "mscorlib" or "netstandard" or "Rask"
               || name.StartsWith("System.", StringComparison.Ordinal)
               || name.StartsWith("Microsoft.", StringComparison.Ordinal)
               || (name.StartsWith("Rask.", StringComparison.Ordinal)
                   && !name.EndsWith(".Tests", StringComparison.Ordinal)
                   && name is not "Rask.Site");
    }
}
