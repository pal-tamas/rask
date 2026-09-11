using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Rask.Cqrs;

/// <summary>Registers Rask.Cqrs into an <see cref="IServiceCollection"/>.</summary>
public static class CqrsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IDispatcher"/> and every source-generated handler discovered in the
    /// loaded assemblies, plus any pipeline behaviors configured on <see cref="CqrsOptions"/>. Call
    /// once at startup. Host-agnostic — the same call works on the Rask Server and WASM hosts.
    /// </summary>
    /// <remarks>
    /// <see cref="CqrsOptions"/> reads the <c>Rask:Cqrs</c> configuration section first and then
    /// <paramref name="configure"/>, so code wins. Unlike every other Rask section it is read <b>here, while
    /// registering</b>: the handler lifetime and the validation switch decide which services exist, and that cannot
    /// change once the container is built. It comes from the host builder's configuration as it stands at this call —
    /// appsettings, environment variables and user secrets are all loaded by then — and a container that is not a
    /// host (a test, a browser app) reads none. Pipeline behaviors can only be added in code.
    /// </remarks>
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
        ApplyConfiguration(options, HostConfiguration(services));
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

        // Validation goes on FIRST, so it is the outermost wrapper: a request that is not valid should
        // not reach a transaction, a log line saying it was handled, or the handler. An app that has
        // configured its own behaviors still gets them inside this one, which is the order they would
        // have chosen anyway.
        if (options.ValidateRequests)
        {
            services.Add(new ServiceDescriptor(
                typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>), options.HandlerLifetime));
        }

        // Apply user-configured behaviors in registration order (first-registered runs outermost).
        // BehaviorRegistration keeps the [DynamicallyAccessedMembers] annotation on the implementation
        // type, so this stays trim-safe (no IL2077 on the WASM publish).
        foreach (var behavior in options.Behaviors)
        {
            services.Add(new ServiceDescriptor(behavior.ServiceType, behavior.ImplementationType, options.HandlerLifetime));
        }

        return services;
    }

    /// <summary>Reads the <c>Rask:Cqrs</c> section onto <paramref name="options"/>, key by key.</summary>
    /// <remarks>
    /// Parsed by hand rather than bound: this package runs in the browser too, and four scalars do not warrant putting
    /// the configuration binder into every WASM bundle that dispatches a message.
    /// </remarks>
    internal static void ApplyConfiguration(CqrsOptions options, IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return;
        }

        var section = configuration.GetSection("Rask:Cqrs");

        if (section[nameof(CqrsOptions.HandlerLifetime)] is { Length: > 0 } lifetime)
        {
            options.HandlerLifetime = ParseName<ServiceLifetime>(lifetime, nameof(CqrsOptions.HandlerLifetime));
        }

        if (section[nameof(CqrsOptions.NotificationPublishStrategy)] is { Length: > 0 } strategy)
        {
            options.NotificationPublishStrategy =
                ParseName<NotificationPublishStrategy>(strategy, nameof(CqrsOptions.NotificationPublishStrategy));
        }

        if (section[nameof(CqrsOptions.StopOnFirstNotificationException)] is { Length: > 0 } stop)
        {
            options.StopOnFirstNotificationException = ParseBool(stop, nameof(CqrsOptions.StopOnFirstNotificationException));
        }

        if (section[nameof(CqrsOptions.ValidateRequests)] is { Length: > 0 } validate)
        {
            options.ValidateRequests = ParseBool(validate, nameof(CqrsOptions.ValidateRequests));
        }
    }

    // The host's configuration as it stands at this call. HostApplicationBuilder (and so WebApplicationBuilder) and the
    // generic HostBuilder register their HostBuilderContext as an INSTANCE, and it carries the app's configuration.
    // IConfiguration itself is registered through a factory, so it cannot be read before the container exists. Keyed
    // descriptors are skipped before ImplementationInstance is read, because that property throws on one.
    private static IConfiguration? HostConfiguration(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(HostBuilderContext)
                && !descriptor.IsKeyedService
                && descriptor.ImplementationInstance is HostBuilderContext context)
            {
                return context.Configuration;
            }
        }

        return null;
    }

    // Names only: Enum.TryParse on its own would also accept "7", which names nothing anyone would write on purpose.
    private static TEnum ParseName<TEnum>(string value, string key)
        where TEnum : struct, Enum =>
        !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
        && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Rask:Cqrs:{key} is '{value}'; use one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");

    private static bool ParseBool(string value, string key) =>
        bool.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Rask:Cqrs:{key} is '{value}'; use true or false.");

    // Sentinel marking that AddRaskCqrs already ran on this collection.
    private sealed class CqrsMarker;
}
