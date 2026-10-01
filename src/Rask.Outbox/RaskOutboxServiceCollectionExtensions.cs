using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Rask.Batteries;
using Rask.Cqrs;
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
    /// Every <c>IDurableHandler</c> then runs from here: its event is stored in the same transaction as the change
    /// that raised it (or in its own, when published straight through the dispatcher), and run after the commit,
    /// retried until it succeeds. <c>IEventHandler</c>s keep running in memory; each handler chooses.
    /// </remarks>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the outbox table.</typeparam>
    public static IServiceCollection AddRaskOutbox<TContext>(this IServiceCollection services, Action<OutboxOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // Validated when the host starts, the way AddRaskJobs/AddRaskMail/AddRaskCache are. Without it a value
        // like PollInterval = Zero would spin the processor against the database, and a negative one would throw
        // on the background thread, tearing the host down at an unrelated moment.
        services.AddRaskOptions<OutboxOptions>("Rask:Outbox", static (section, o) => section.Bind(o), configure, validate: null);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>());
        services.TryAddSingleton(Clock.TimeProvider); // Rask's clock, so Clock.Fake moves this battery's time too
        services.TryAddSingleton<OutboxMetrics>();

        // Rask.Data clears the raised events after the save; without it they would be stored again on the next one.
        services.AddRaskData();

        // The signal is the idempotency marker too: a second call finds it and adds no second interceptor.
        if (!services.Any(static d => d.ServiceType == typeof(OutboxSignal)))
        {
            services.AddSingleton(static _ => new OutboxSignal());
            services.AddSingleton<ISaveChangesInterceptor>(static sp =>
                new OutboxInterceptor(sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<OutboxSignal>()));
        }

        // Where a bare dispatcher.Publish stores an event for its durable handlers.
        services.TryAddSingleton<IDurableEventStore, OutboxEventStore<TContext>>();

        // Before the processor, so an app whose model never mapped OutboxMessage fails the boot with the
        // line to type rather than on the first domain event. The processor itself tolerates a missing
        // table — it has to, because a freshly scaffolded app boots before its first migration has run —
        // so it is the wrong place to notice. See BatteryModelCheck: this reads the MODEL, never the
        // database, so an app that has not run `rask db update` yet still starts.
        services.AddHostedService<OutboxModelCheck<TContext>>();

        // The poll is Rask's bookkeeping, not the application's query log, so it runs on a context
        // whose SQL logs at Debug — see HousekeepingContextFactory. Deduplicates on repeat, as
        // AddHostedService does.
        services.AddHousekeepingService<OutboxProcessor<TContext>, TContext>();
        return services;
    }
}
