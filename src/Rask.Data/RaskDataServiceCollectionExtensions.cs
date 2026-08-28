using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Data;

/// <summary>Registers Rask.Data's EF Core interceptors into an <see cref="IServiceCollection"/>.</summary>
public static class RaskDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the auditing, soft-delete, and (unless disabled) domain-event interceptors as
    /// <see cref="ISaveChangesInterceptor"/> services, plus a <see cref="TimeProvider"/>. Add them to a
    /// context with
    /// <c>o.AddInterceptors(sp.GetServices&lt;ISaveChangesInterceptor&gt;())</c> in your
    /// <c>AddDbContext(Factory)</c> callback, and call <c>modelBuilder.ApplyRaskConventions()</c> in
    /// <c>OnModelCreating</c>. Idempotent. Domain-event dispatch needs <c>AddRaskCqrs()</c>.
    /// </summary>
    /// <remarks>
    /// Domain events are published in-process unless something else owns delivery. Registering
    /// <c>Rask.Outbox</c> is enough to hand delivery over — it registers an
    /// <see cref="IDomainEventDeliveryOwner"/>, and this method needs no argument to match. The handover is
    /// resolved when the container is built, so it holds whichever order the two <c>Add</c> calls appear
    /// in. Override it in either direction with
    /// <see cref="RaskDataOptions.DispatchDomainEventsInProcess"/>.
    /// </remarks>
    public static IServiceCollection AddRaskData(this IServiceCollection services, Action<RaskDataOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RaskDataOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(TimeProvider.System);

        // The interceptor reads this to honour an explicit DispatchDomainEventsInProcess. TryAdd keeps the
        // first registration, matching the idempotence of the interceptor block below.
        services.TryAddSingleton(options);

        // Registration order is the interception order: soft-delete rewrites Deleted -> Modified first, so
        // auditing then stamps + versions the resulting update.
        if (!services.Any(static d => d.ImplementationType == typeof(SoftDeleteInterceptor)))
        {
            services.AddSingleton<ISaveChangesInterceptor, SoftDeleteInterceptor>();
            services.AddSingleton<ISaveChangesInterceptor, AuditingInterceptor>();

            // Registered unless the caller has explicitly said "never". WHETHER IT PUBLISHES is not decided
            // here: DomainEventInterceptor asks the built container whether anything owns delivery. Deciding
            // it at this line would freeze the answer before AddRaskOutbox has necessarily run, which is
            // exactly the order-dependent silent failure this replaces.
            if (options.DispatchDomainEventsInProcess is not false)
            {
                services.AddSingleton<ISaveChangesInterceptor, DomainEventInterceptor>();
            }
        }

        return services;
    }
}
