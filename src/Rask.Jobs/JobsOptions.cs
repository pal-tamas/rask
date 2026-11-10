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
    /// This is the recovery window, not a timeout: overrunning it cancels nothing. The instance running a job
    /// renews its lease every third of this while the job runs, so a job longer than the lease is never picked
    /// up by a second instance — the new container of a deploy included. A processor that dies mid-job stops
    /// renewing, and its work is claimable again after this long. To stop a job that runs too long, give it a
    /// time limit — <see cref="Limit{TJob}" />, or <see cref="StopAfter" /> for every job — or stop it by hand
    /// with <c>Jobs.Stop(id)</c>.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How many times to attempt a failing job before it is left as a dead letter (kept for inspection). Default 25.</summary>
    public int MaxAttempts { get; set; } = 25;

    /// <summary>The base delay before the first retry; each further retry doubles it (capped at <see cref="MaxRetryDelay"/>). Default 10s.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The cap on the exponential retry backoff. Default 1h.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long completed jobs are kept before being purged. <see cref="TimeSpan.Zero"/> keeps them forever. Default 7 days.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long a job that is already running may keep running after the host is asked to stop.
    /// <para>
    /// On <c>SIGTERM</c> the processor immediately stops picking up <em>new</em> jobs — not the next one in
    /// its batch either — but the one already in your handler keeps a live cancellation token. By default
    /// (<see cref="Timeout.InfiniteTimeSpan"/>) that token is cancelled only when the host's own shutdown
    /// budget, <c>HostOptions.ShutdownTimeout</c>, runs out; <c>rask deploy</c> sizes that budget from the
    /// app's <c>stopTimeout</c> (default 30 minutes), so a job that is halfway through programming a device
    /// finishes instead of being torn in two.
    /// </para>
    /// <para>
    /// A job cancelled at the end of the budget re-runs from the top on the next boot. It does <b>not</b>
    /// count a failed attempt: a redeploy is not a failure, and counting it would march never-failing work
    /// toward its dead letter at the cadence you deploy. Handlers must be idempotent either way.
    /// </para>
    /// <para>
    /// A finite value cancels the job that long after <c>SIGTERM</c> instead, and still no later than the
    /// host's budget; <see cref="TimeSpan.Zero"/> cancels immediately.
    /// </para>
    /// </summary>
    public TimeSpan ShutdownGracePeriod { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// The time limit of every job whose type has none of its own (<see cref="Limit{TJob}" />): a job still running this
    /// long after its handler started is stopped (<see cref="JobStop.TimeLimit" />), ends stopped and is not tried again.
    /// <see cref="Timeout.InfiniteTimeSpan" /> — the default — is no limit: a backup that takes all night is not cut off
    /// because nobody thought to name it. Bound from <c>Rask:Jobs:StopAfter</c>.
    /// </summary>
    public TimeSpan StopAfter { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// The zone a calendar schedule's wall-clock time is read in — <c>.Daily.At(3, 0)</c> means 3am here.
    /// Defaults to UTC, so the same app keeps the same schedule on every machine it is deployed to; set it
    /// when "3am" has to mean 3am where your customers are, and it will follow daylight saving.
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// What the queue and the processor of this app say to each other about an awaited job. Carried here because these
    /// options are the one instance both are handed — the processor is built by <c>ActivatorUtilities</c>, so it can take
    /// nothing internal through its constructor.
    /// </summary>
    internal JobSignals Signals { get; } = new();

    /// <summary>The registered recurring jobs.</summary>
    internal List<RecurringJob> Recurring { get; } = [];

    /// <summary>The limits declared in code, by the job's registered type name.</summary>
    internal Dictionary<string, JobLimit> Limits { get; } = new(StringComparer.Ordinal);

    /// <summary>The limits read from <c>Rask:Jobs:Limit:&lt;TypeName&gt;</c>, by the job's short type name.</summary>
    internal Dictionary<string, JobLimit> ConfiguredLimits { get; } = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Says when a running <typeparamref name="TJob"/> stops by itself — the limit is a step:
    /// <c>Limit&lt;AtPump&gt;().After(30.Minutes)</c>, <c>Limit&lt;AtPump&gt;().After(job =&gt; job.Cap)</c>,
    /// <c>Limit&lt;Import&gt;().IdleFor(5.Minutes)</c>. The bare call sets nothing. A job stopped at a limit ends stopped,
    /// not failed: it is not tried again. <c>Rask:Jobs:Limit:&lt;TypeName&gt;:After</c> and <c>…:IdleFor</c> set the same
    /// limits from configuration; what code says wins.
    /// </summary>
    /// <typeparam name="TJob">The job the limits hold for.</typeparam>
    public Limiting<TJob> Limit<TJob>()
        where TJob : IJob
    {
        var key = JobSerializerRegistry.TypeName(typeof(TJob));
        if (!Limits.TryGetValue(key, out var rule))
        {
            rule = new JobLimit();
            Limits[key] = rule;
        }

        return new Limiting<TJob>(rule);
    }

    /// <summary>
    /// Machinery: takes in the limits declared somewhere else — on the battery, as <c>c.Jobs.Limit&lt;T&gt;()</c> — part by
    /// part, so a later declaration of a limit wins over an earlier one.
    /// </summary>
    /// <param name="declared">The options the limits were declared on.</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void AdoptLimits(JobsOptions declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        foreach (var (type, rule) in declared.Limits)
        {
            if (!Limits.TryGetValue(type, out var mine))
            {
                mine = new JobLimit();
                Limits[type] = mine;
            }

            mine.Take(rule);
        }
    }

    /// <summary>
    /// The limits of <paramref name="job"/>, registered as <paramref name="type"/>: code's first, then configuration's, part by
    /// part, and <see cref="StopAfter"/> for a time limit neither names.
    /// </summary>
    internal (TimeSpan? After, TimeSpan? IdleFor) LimitsOf(string type, IJob job)
    {
        Limits.TryGetValue(type, out var code);
        ConfiguredLimits.TryGetValue(JobSerializerRegistry.ShortName(type), out var configured);

        var rule = code is { LimitsTime: true } ? code : configured;
        TimeSpan? appWide = StopAfter == Timeout.InfiniteTimeSpan ? null : StopAfter;
        var after = rule is { LimitsTime: true } ? rule.AfterFor(job) : appWide;
        return (after, code?.IdleFor ?? configured?.IdleFor);
    }
}
