namespace Rask.Background;

/// <summary>Options for the <see cref="JobProcessor{TContext}"/>.</summary>
public sealed class JobsOptions
{
    /// <summary>The ceiling on <see cref="BatchSize"/> — see <see cref="JobsOptionsValidator"/> for why there is one.</summary>
    internal const int MaxBatchSize = 1000;

    /// <summary>How often the processor polls the jobs table for due work. Default 5s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many jobs to claim and run per poll. Default 100, maximum 1000.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How long a claimed job stays invisible to other processor instances. Default 5 minutes.
    /// </summary>
    /// <remarks>
    /// This is the recovery window, not a timeout: nothing cancels a job that overruns it. A processor
    /// that dies mid-job makes its work claimable again after this long, so it must comfortably exceed the
    /// longest job you run — set it too low and a slow job is picked up by a second instance while the
    /// first is still working on it, which is the duplicate the lease exists to prevent.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How many times to attempt a failing job before it is left as a dead letter (kept for inspection). Default 25.</summary>
    public int MaxAttempts { get; set; } = 25;

    /// <summary>The base delay before the first retry; each further retry doubles it (capped at <see cref="MaxRetryDelay"/>). Default 10s.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The cap on the exponential retry backoff. Default 1h.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long completed jobs are kept before being purged. <see cref="TimeSpan.Zero"/> keeps them forever. Default 7 days.</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long a job that is already running may keep running after the host is asked to stop.
    /// <para>
    /// On <c>SIGTERM</c> the processor immediately stops picking up <em>new</em> jobs, but the one already
    /// in your handler is given this long to finish rather than being cancelled mid-call — so a job that
    /// is halfway through a <c>SaveChangesAsync</c> completes instead of being torn in two.
    /// </para>
    /// <para>
    /// A job that outlives the grace is cancelled and re-runs from the top on the next boot. It does
    /// <b>not</b> count a failed attempt: a redeploy is not a failure, and counting it would march
    /// never-failing work toward its dead letter at the cadence you deploy. Handlers must be idempotent
    /// either way — there is no lease or claim column, so an interrupted job always re-runs whole.
    /// </para>
    /// <para>
    /// Cannot exceed <c>HostOptions.ShutdownTimeout</c>: once that elapses the host stops waiting for
    /// hosted services, so a grace longer than it silently does not happen. Default 5s;
    /// <see cref="TimeSpan.Zero"/> cancels immediately.
    /// </para>
    /// </summary>
    public TimeSpan ShutdownGracePeriod { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The zone a calendar schedule's wall-clock time is read in — <c>.Daily.At(3, 0)</c> means 3am here.
    /// Defaults to UTC, so the same app keeps the same schedule on every machine it is deployed to; set it
    /// when "3am" has to mean 3am where your customers are, and it will follow daylight saving.
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>The registered recurring jobs.</summary>
    internal List<RecurringJob> Recurring { get; } = [];

    /// <summary>
    /// The registered recurring jobs, in registration order — the schedule an operator or an ops dashboard
    /// reads to answer "what is supposed to run, and how often?". Pair each entry with the
    /// <see cref="RecurringJobState"/> row of the same <see cref="RecurringJob.Name"/> to see when it last
    /// fired, and call <see cref="RecurringJob.Factory"/> to enqueue an off-schedule run.
    /// </summary>
    public IReadOnlyList<RecurringJob> RecurringJobs => Recurring;

    /// <summary>
    /// The delay before the next retry of a job on its <paramref name="attempts"/>-th attempt: an
    /// exponential backoff (<see cref="BaseRetryDelay"/> × 2^(attempts-1)) capped at <see cref="MaxRetryDelay"/>.
    /// Pure and deterministic.
    /// </summary>
    internal TimeSpan RetryDelay(int attempts)
    {
        if (attempts <= 1)
        {
            return BaseRetryDelay;
        }

        var scaled = BaseRetryDelay.Ticks * Math.Pow(2, attempts - 1);
        return double.IsInfinity(scaled) || scaled >= MaxRetryDelay.Ticks
            ? MaxRetryDelay
            : TimeSpan.FromTicks((long)scaled);
    }

    /// <summary>
    /// Runs <typeparamref name="TJob"/> on a schedule, durably — the cadence is a step:
    /// <c>Run&lt;PurgeStaleCarts&gt;().Every(1.Hour)</c>, <c>Run&lt;Backup&gt;().Daily.At(3, 0)</c>,
    /// <c>Run&lt;Digest&gt;().Weekly.On(DayOfWeek.Monday).At(9, 0)</c>.
    /// </summary>
    /// <typeparam name="TJob">The job to enqueue on each tick. A fresh one is built per run.</typeparam>
    public RecurringJob Run<TJob>()
        where TJob : IJob, new() => Run(() => new TJob());

    /// <summary>
    /// Runs the job <paramref name="make"/> builds on a schedule, for a job that needs arguments:
    /// <c>Run(() =&gt; new Digest(Top: 10)).Weekly.On(DayOfWeek.Monday).At(9, 0)</c>.
    /// </summary>
    /// <typeparam name="TJob">The job to enqueue on each tick.</typeparam>
    public RecurringJob Run<TJob>(Func<TJob> make)
        where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(make);
        var job = new RecurringJob(typeof(TJob).Name, () => make(), Recurring);
        job.RefuseADuplicateName(job.Name);
        Recurring.Add(job);
        return job;
    }

    /// <summary>
    /// Machinery: takes in a schedule declared somewhere else — on the battery, as <c>c.Jobs.Run&lt;T&gt;()</c> — so it
    /// runs beside the ones declared here, under the same rule that two schedules may not share a name.
    /// </summary>
    /// <param name="job">The declared schedule.</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void Adopt(RecurringJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var adopted = new RecurringJob(job.Name, job.Factory, Recurring);
        adopted.RefuseADuplicateName(job.Name);
        Recurring.Add(job.Schedule is { } schedule ? adopted.With(schedule) : adopted);
    }
}
