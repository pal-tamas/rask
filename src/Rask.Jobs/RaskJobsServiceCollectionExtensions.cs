using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Rask.Batteries;
using Rask.Hosting.Shared;

namespace Rask.Background;

/// <summary>Registers background jobs into an <see cref="IServiceCollection"/>.</summary>
public static class RaskJobsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IJobs"/> and the background <see cref="JobProcessor{TContext}"/>. Map the
    /// tables with <c>modelBuilder.AddRaskJobs()</c> in <c>OnModelCreating</c>, register your context as an
    /// <see cref="IDbContextFactory{TContext}"/>, and add <c>AddRaskCqrs()</c> (jobs dispatch to their
    /// <see cref="IJobHandler{TJob}"/> through it). Idempotent.
    /// </summary>
    /// <remarks>
    /// <see cref="JobsOptions"/> reads the <c>Rask:Jobs</c> configuration section first (so
    /// <c>Rask__Jobs__MaxAttempts=5</c> in the environment works), then <paramref name="configure"/>, so code wins.
    /// A bad value fails the host's start, naming the key.
    /// </remarks>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the jobs tables.</typeparam>
    public static IServiceCollection AddRaskJobs<TContext>(this IServiceCollection services, Action<JobsOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // The binding generator cannot bind the recurring schedule (see the NoWarn in Rask.Jobs.csproj): it is a
        // list of job factories, which appsettings has no way to express, so Run<TJob>() is the only way to
        // fill it. Every other property binds, and RaskJobsOptionsBindingTests pins that each settable one does.
        services.AddRaskOptions("Rask:Jobs", static (section, o) => Bind(section, o), configure, validate: null);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JobsOptions>, JobsOptionsValidator>());
        services.TryAddSingleton(Clock.TimeProvider); // Rask's clock, so Clock.Fake moves this battery's time too
        services.TryAddSingleton<JobMetrics>();
        services.TryAddSingleton<IJobs, JobBacklog<TContext>>();

        // Before the processor, so an app whose model never mapped Job fails the boot with the line to
        // type rather than on the first enqueue. The processor itself tolerates a missing table — it has
        // to, because a freshly scaffolded app boots before its first migration has run — so it is the
        // wrong place to notice. See BatteryModelCheck: this reads the MODEL, never the database.
        services.AddHostedService<JobsModelCheck<TContext>>();

        // The poll is Rask's bookkeeping, not the application's query log, so it runs on a context
        // whose SQL logs at Debug — see HousekeepingContextFactory. Deduplicates on repeat, as
        // AddHostedService does.
        services.AddHousekeepingService<JobProcessor<TContext>, TContext>();
        return services;
    }

    /// <summary>
    /// Binds <c>Rask:Jobs</c> onto <paramref name="options"/>, including the one key the binder cannot do
    /// on its own: <see cref="TimeZoneInfo"/> has no converter, so <c>Rask__Jobs__TimeZone=Europe/Budapest</c>
    /// would otherwise be read, skipped, and silently leave every calendar schedule on UTC.
    /// </summary>
    private static void Bind(IConfiguration section, JobsOptions options)
    {
        section.Bind(options);
        BindLimits(section.GetSection("Limit"), options);

        if (section["TimeZone"] is not { Length: > 0 } id)
        {
            return;
        }

        try
        {
            options.TimeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"Rask:Jobs:TimeZone is '{id}', which this machine has no time zone for. Use an IANA id such "
                + "as 'Europe/Budapest', or leave it unset for UTC.",
                ex);
        }
    }

    /// <summary>
    /// Binds <c>Rask:Jobs:Limit:&lt;TypeName&gt;:After</c> and <c>…:IdleFor</c>: a dictionary keyed by the job's short type name,
    /// which the binder would read into nothing, because the limits are not a property the options expose.
    /// </summary>
    private static void BindLimits(IConfigurationSection limits, JobsOptions options)
    {
        foreach (var type in limits.GetChildren())
        {
            var rule = new JobLimit
            {
                After = Duration(type, "After"),
                IdleFor = Duration(type, "IdleFor"),
            };
            if (options.ConfiguredLimits.TryGetValue(type.Key, out var bound))
            {
                bound.Take(rule);
            }
            else
            {
                options.ConfiguredLimits[type.Key] = rule;
            }
        }
    }

    private static TimeSpan? Duration(IConfigurationSection type, string key)
    {
        if (type[key] is not { Length: > 0 } value)
        {
            return null;
        }

        return TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var duration)
            ? duration
            : throw new InvalidOperationException(
                $"Rask:Jobs:Limit:{type.Key}:{key} is '{value}', which is not a duration. Write it as hh:mm:ss: '00:30:00' is "
                + "thirty minutes.");
    }
}
