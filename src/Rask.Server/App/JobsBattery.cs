using System.Runtime.CompilerServices;
using Rask.Background;

namespace Rask;

/// <summary>
///     Schedules written straight on the battery: <c>c.Jobs.Run&lt;PurgeStaleCarts&gt;().Every(1.Hour)</c>, the same
///     steps <see cref="JobsOptions.Run{TJob}()" /> takes, without a <c>Configure(o =&gt; …)</c> around them.
/// </summary>
public static class JobsBattery
{
    // The jobs a battery has declared, held until the battery is applied: the options they belong to do not exist
    // while Program.cs is still describing the app. Keyed weakly, so a battery nobody holds takes its jobs with it.
    private static readonly ConditionalWeakTable<Battery<JobsOptions>, JobsOptions> Declared = new();

    extension(Battery<JobsOptions> jobs)
    {
        /// <summary>Runs <typeparamref name="TJob" /> on the schedule the next step names.</summary>
        /// <typeparam name="TJob">The job to enqueue on each tick. A fresh one is built per run.</typeparam>
        /// <returns>The schedule's steps: <c>.Every(1.Hour)</c>, <c>.Daily.At(3, 0)</c>, <c>.Weekly.On(…)</c>.</returns>
        public RecurringJob Run<TJob>()
            where TJob : IJob, new() => jobs.Run(() => new TJob());

        /// <summary>Runs the job <paramref name="make" /> builds on the schedule the next step names.</summary>
        /// <param name="make">Builds the job for each run, for one that needs arguments.</param>
        /// <typeparam name="TJob">The job to enqueue on each tick.</typeparam>
        /// <returns>The schedule's steps.</returns>
        public RecurringJob Run<TJob>(Func<TJob> make)
            where TJob : IJob
        {
            ArgumentNullException.ThrowIfNull(make);
            return Declared.GetValue(jobs, Stage).Run(make);
        }

        /// <summary>
        ///     Says when a running <typeparamref name="TJob" /> stops by itself, in the step that follows:
        ///     <c>c.Jobs.Limit&lt;AtPump&gt;().After(30.Minutes).IdleFor(5.Minutes)</c>.
        /// </summary>
        /// <typeparam name="TJob">The job the limits hold for.</typeparam>
        /// <returns>The limit's steps: <c>.After(…)</c>, <c>.IdleFor(…)</c>.</returns>
        public Limiting<TJob> Limit<TJob>()
            where TJob : IJob => Declared.GetValue(jobs, Stage).Limit<TJob>();
    }

    // One Configure per battery, however many jobs and limits it declares: it copies each into the options being applied, so
    // the duplicate-name rule sees them beside any declared inside Configure(o => o.Run<…>()), and a limit wins over config.
    private static JobsOptions Stage(Battery<JobsOptions> jobs)
    {
        var declared = new JobsOptions();
        jobs.Configure(options =>
        {
            foreach (var job in declared.RecurringJobs)
            {
                options.Adopt(job);
            }

            options.AdoptLimits(declared);
        });

        return declared;
    }
}
