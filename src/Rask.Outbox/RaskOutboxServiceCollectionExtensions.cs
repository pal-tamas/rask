using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Outbox;

/// <summary>Registers the transactional outbox into an <see cref="IServiceCollection"/>.</summary>
public static class RaskOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="OutboxInterceptor"/> (add it to your context with
    /// <c>o.AddInterceptors(sp.GetServices&lt;ISaveChangesInterceptor&gt;())</c>) and the background
    /// <see cref="OutboxProcessor{TContext}"/>. Map the table with <c>modelBuilder.AddRaskOutbox()</c> in
    /// <c>OnModelCreating</c>, and — so events aren't delivered twice — disable Rask.Data's in-process
    /// publisher with <c>AddRaskData(o =&gt; o.DispatchDomainEventsInProcess = false)</c>. Idempotent.
    /// </summary>
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

        if (!services.Any(static d => d.ImplementationType == typeof(OutboxInterceptor)))
        {
            services.AddSingleton<ISaveChangesInterceptor, OutboxInterceptor>();
        }

        services.AddHostedService<OutboxProcessor<TContext>>();
        return services;
    }
}
