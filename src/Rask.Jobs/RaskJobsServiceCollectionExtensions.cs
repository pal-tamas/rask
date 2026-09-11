using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Hosting.Shared;

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
    /// <remarks>
    /// <see cref="JobOptions"/> reads the <c>Rask:Jobs</c> configuration section first (so
    /// <c>Rask__Jobs__MaxAttempts=5</c> in the environment works), then <paramref name="configure"/>, so code wins.
    /// A bad value fails the host's start, naming the key.
    /// </remarks>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the jobs tables.</typeparam>
    public static IServiceCollection AddRaskJobs<TContext>(this IServiceCollection services, Action<JobOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // The binding generator cannot bind the recurring schedule (see the NoWarn in Rask.Jobs.csproj): it is a
        // list of job factories, which appsettings has no way to express, so AddRecurring is the only way to
        // fill it. Every other property binds, and RaskJobsOptionsBindingTests pins that each settable one does.
        services.AddRaskOptions("Rask:Jobs", static (section, o) => section.Bind(o), configure, static o => o.Validate());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<JobMetrics>();
        services.TryAddSingleton<IJob, JobQueue<TContext>>();

        // Before the processor, so an app whose model never mapped Job fails the boot with the line to
        // type rather than on the first enqueue. The processor itself tolerates a missing table — it has
        // to, because a freshly scaffolded app boots before its first migration has run — so it is the
        // wrong place to notice. See BatteryModelCheck: this reads the MODEL, never the database.
        services.AddHostedService<JobsModelCheck<TContext>>();

        // AddHostedService uses TryAddEnumerable, so a repeated call registers only one processor.
        services.AddHostedService<JobProcessor<TContext>>();
        return services;
    }
}
