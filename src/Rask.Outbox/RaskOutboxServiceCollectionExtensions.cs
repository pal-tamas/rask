using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Data;

namespace Rask.Outbox;

/// <summary>Registers the transactional outbox into an <see cref="IServiceCollection"/>.</summary>
public static class RaskOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="OutboxInterceptor"/> (add it to your context with
    /// <c>o.AddInterceptors(sp.GetServices&lt;ISaveChangesInterceptor&gt;())</c>) and the background
    /// <see cref="OutboxProcessor{TContext}"/>. Map the table with <c>modelBuilder.AddRaskOutbox()</c> in
    /// <c>OnModelCreating</c>. Idempotent.
    /// </summary>
    /// <remarks>
    /// This call is all it takes to hand domain-event delivery to the outbox: it registers an
    /// <see cref="IDomainEventDeliveryOwner"/>, which makes Rask.Data's in-process publisher stand down.
    /// No second argument on <c>AddRaskData</c>, and no ordering requirement between the two calls — the
    /// handover is resolved when the container is built. Setting
    /// <see cref="RaskDataOptions.DispatchDomainEventsInProcess"/> to <c>true</c> overrides it, which
    /// re-creates the double-delivery this prevents.
    /// </remarks>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the outbox table.</typeparam>
    public static IServiceCollection AddRaskOutbox<TContext>(this IServiceCollection services, Action<OutboxOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new OutboxOptions();
        configure?.Invoke(options);
        // Fail fast here, the way AddRaskJobs/AddRaskMail/AddRaskCache already do. Without this a value
        // like PollInterval = Zero throws out of `new PeriodicTimer(...)` on the background thread, which
        // (BackgroundServiceExceptionBehavior.StopHost) tears the host down at an unrelated moment.
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OutboxMetrics>();

        // Take ownership of domain-event delivery. Rask.Data's DomainEventInterceptor reads this from the
        // BUILT container, so it holds whether this call comes before or after AddRaskData — the ordering
        // that used to decide, silently and wrongly, whether the outbox ever received anything.
        services.TryAddSingleton<IDomainEventDeliveryOwner, OutboxDeliveryOwner>();

        if (!services.Any(static d => d.ImplementationType == typeof(OutboxInterceptor)))
        {
            services.AddSingleton<ISaveChangesInterceptor, OutboxInterceptor>();
        }

        services.AddHostedService<OutboxProcessor<TContext>>();
        return services;
    }
}
