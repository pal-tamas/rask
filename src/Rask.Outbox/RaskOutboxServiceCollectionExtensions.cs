using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Data;
using Rask.Hosting.Shared;

namespace Rask.Outbox;

/// <summary>Registers the transactional outbox into an <see cref="IServiceCollection"/>.</summary>
public static class RaskOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="OutboxInterceptor"/> (add it to your context with
    /// <c>o.AddInterceptors(sp.GetServices&lt;ISaveChangesInterceptor&gt;())</c>) and the background
    /// <see cref="OutboxProcessor{TContext}"/>. Map the table with <c>modelBuilder.AddRaskOutbox()</c> in
    /// <c>OnModelCreating</c>. <see cref="OutboxOptions"/> reads the <c>Rask:Outbox</c> configuration section first
    /// and then <paramref name="configure"/>, so code wins. Idempotent.
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

        // Validated when the host starts, the way AddRaskJobs/AddRaskMail/AddRaskCache are. Without it a value
        // like PollInterval = Zero throws out of `new PeriodicTimer(...)` on the background thread, which
        // (BackgroundServiceExceptionBehavior.StopHost) tears the host down at an unrelated moment.
        services.AddRaskOptions<OutboxOptions>("Rask:Outbox", static (section, o) => section.Bind(o), configure,
            static o => o.Validate());
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

        // Before the processor, so an app whose model never mapped OutboxMessage fails the boot with the
        // line to type rather than on the first domain event. The processor itself tolerates a missing
        // table — it has to, because a freshly scaffolded app boots before its first migration has run —
        // so it is the wrong place to notice. See BatteryModelCheck: this reads the MODEL, never the
        // database, so an app that has not run `rask db update` yet still starts.
        services.AddHostedService<OutboxModelCheck<TContext>>();

        services.AddHostedService<OutboxProcessor<TContext>>();
        return services;
    }
}
