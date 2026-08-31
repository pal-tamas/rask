using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Jobs;

/// <summary>Registers background jobs into an <see cref="IServiceCollection"/>.</summary>
public static class RaskJobsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IJob"/> and the background <see cref="JobProcessor{TContext}"/>. Map the
    /// tables with <c>modelBuilder.AddRaskJobs()</c> in <c>OnModelCreating</c>, register your context as an
    /// <see cref="IDbContextFactory{TContext}"/>, and add <c>AddRaskCqrs()</c> (jobs dispatch to their
    /// <c>ICommandHandler</c> through it). Idempotent.
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the jobs tables.</typeparam>
    public static IServiceCollection AddRaskJobs<TContext>(this IServiceCollection services, Action<JobOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new JobOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<JobMetrics>();
        services.TryAddSingleton<IJob, JobQueue<TContext>>();

        // AddHostedService uses TryAddEnumerable, so a repeated call registers only one processor.
        services.AddHostedService<JobProcessor<TContext>>();
        return services;
    }
}
