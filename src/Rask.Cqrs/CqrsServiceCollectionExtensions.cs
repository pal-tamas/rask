using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Cqrs;

/// <summary>Registers Rask.Cqrs into an <see cref="IServiceCollection"/>.</summary>
public static class CqrsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IDispatcher"/> and every source-generated handler discovered in the
    /// loaded assemblies, plus any pipeline behaviors configured on <see cref="CqrsOptions"/>. Call
    /// once at startup. Host-agnostic — the same call works on the Rask Server and WASM hosts.
    /// </summary>
    public static IServiceCollection AddRaskCqrs(this IServiceCollection services, Action<CqrsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Idempotent: a second call (e.g. a shared library and the app host both register) is a no-op,
        // so behaviors aren't double-registered and the first call's options win consistently.
        if (services.Any(static d => d.ServiceType == typeof(CqrsMarker)))
        {
            return services;
        }

        services.AddSingleton(new CqrsMarker());

        var options = new CqrsOptions();
        configure?.Invoke(options);
        options.Validate();

        // The dispatcher is transient so it captures whatever provider resolves it (the per-session
        // scope on Server, the root scope on WASM); it holds no per-session state.
        services.TryAddTransient<Dispatcher>();
        services.TryAddTransient<IDispatcher>(static sp => sp.GetRequiredService<Dispatcher>());

        services.TryAddSingleton(new CqrsExecutionOptions
        {
            PublishStrategy = options.NotificationPublishStrategy,
            StopOnFirstException = options.StopOnFirstNotificationException,
        });

        // Apply the generated handler registrations (populated by [ModuleInitializer]s at module load).
        CqrsRegistry.ApplyRegistrations(services, options.HandlerLifetime);

        // Apply user-configured behaviors in registration order (first-registered runs outermost).
        // BehaviorRegistration keeps the [DynamicallyAccessedMembers] annotation on the implementation
        // type, so this stays trim-safe (no IL2077 on the WASM publish).
        foreach (var behavior in options.Behaviors)
        {
            services.Add(new ServiceDescriptor(behavior.ServiceType, behavior.ImplementationType, options.HandlerLifetime));
        }

        return services;
    }

    // Sentinel marking that AddRaskCqrs already ran on this collection.
    private sealed class CqrsMarker;
}
