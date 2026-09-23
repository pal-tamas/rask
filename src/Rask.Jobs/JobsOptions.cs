namespace Rask.Jobs;

/// <summary>Options for the <see cref="JobProcessor{TContext}"/>.</summary>
public sealed class JobsOptions
{
    /// <summary>The ceiling on <see cref="BatchSize"/> — see <see cref="Validate"/> for why there is one.</summary>
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
    /// The zone a calendar schedule's wall-clock time is read in — <c>.Daily.At(3, 00)</c> means 3am here.
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

    /// <summary>Validates the option values once <c>Rask:Jobs</c> and the callback have applied (checked at host start, so a bad value fails fast rather than tearing down the host later).</summary>
    internal void Validate()
    {
        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PollInterval), PollInterval, "PollInterval must be positive.");
        }

        if (BatchSize is < 1 or > MaxBatchSize)
        {
            // Capped because the claim sends the candidate ids as an IN list. EF translates a parameterized
            // Contains to json_each / = ANY / OPENJSON rather than one parameter per id, so the classic
            // 999/2100 ceilings shouldn't bite — this is the belt to that pair of braces.
            throw new ArgumentOutOfRangeException(
                nameof(BatchSize), BatchSize, $"BatchSize must be between 1 and {MaxBatchSize}.");
        }

        if (LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(LeaseDuration), LeaseDuration, "LeaseDuration must be positive.");
        }

        if (LeaseDuration <= PollInterval)
        {
            // A lease that expires within one poll guarantees every job is stolen mid-flight by the next
            // instance to look — strictly worse than no lease at all, so it is refused rather than warned about.
            throw new ArgumentOutOfRangeException(
                nameof(LeaseDuration),
                LeaseDuration,
                $"LeaseDuration must be longer than PollInterval ({PollInterval}), or every claimed job is stolen before it finishes.");
        }

        if (MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAttempts), MaxAttempts, "MaxAttempts must be at least 1.");
        }

        if (BaseRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BaseRetryDelay), BaseRetryDelay, "BaseRetryDelay cannot be negative.");
        }

        if (MaxRetryDelay < BaseRetryDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetryDelay), MaxRetryDelay, "MaxRetryDelay cannot be less than BaseRetryDelay.");
        }

        if (RetentionPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RetentionPeriod), RetentionPeriod, "RetentionPeriod cannot be negative.");
        }

        ValidateShutdownGracePeriod(ShutdownGracePeriod);

        // `o.Run<Backup>();` with no cadence compiles and would then never run — the quietest possible
        // failure, so it is refused at boot instead.
        if (Recurring.FirstOrDefault(r => r.Schedule is null) is { } unscheduled)
        {
            throw new InvalidOperationException(
                $"Run<{unscheduled.Name}>() has no schedule. Finish it with .Every(1.Hour), .Daily.At(3, 00), "
                + ".Weekly.On(DayOfWeek.Monday).At(9, 00) or .Monthly.On(1).At(6, 00).");
        }
    }

    /// <summary>
    /// Range check for the shutdown grace. The upper bound is not pedantry:
    /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> throws above <see cref="int.MaxValue"/>
    /// milliseconds, and it would throw from the shutdown path — the worst place to find out. Each
    /// battery carries its own copy; they are independent packages that must not reference each other.
    /// </summary>
    private static void ValidateShutdownGracePeriod(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownGracePeriod), value, "ShutdownGracePeriod cannot be negative (Zero cancels immediately).");
        }

        if (value.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownGracePeriod), value, $"ShutdownGracePeriod must be at most {TimeSpan.FromMilliseconds(int.MaxValue)}.");
        }
    }

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
    /// <c>Run&lt;PurgeStaleCarts&gt;().Every(1.Hour)</c>, <c>Run&lt;Backup&gt;().Daily.At(3, 00)</c>,
    /// <c>Run&lt;Digest&gt;().Weekly.On(DayOfWeek.Monday).At(9, 00)</c>.
    /// </summary>
    /// <typeparam name="TJob">The job to enqueue on each tick. A fresh one is built per run.</typeparam>
    public RecurringJob Run<TJob>()
        where TJob : IJob, new() => Run(() => new TJob());

    /// <summary>
    /// Runs the job <paramref name="make"/> builds on a schedule, for a job that needs arguments:
    /// <c>Run(() =&gt; new Digest(Top: 10)).Weekly.On(DayOfWeek.Monday).At(9, 00)</c>.
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
}

/// <summary>A job on a schedule, still being worded: the cadence comes next.</summary>
/// <remarks>
/// <code>
/// o.Run&lt;PurgeStaleCarts&gt;().Every(1.Hour);
/// o.Run&lt;Backup&gt;().Daily.At(3, 00);
/// o.Run&lt;Digest&gt;().Weekly.On(DayOfWeek.Monday).At(9, 00);
/// o.Run&lt;CloseBooks&gt;().Monthly.On(1).At(6, 00);
/// </code>
/// <para>
/// The durable name defaults to the job's type name; <see cref="Named"/> overrides it. It is what the
/// <see cref="RecurringJobState"/> row is keyed by, so renaming the type — or the name — re-arms the job
/// as if it had never run.
/// </para>
/// </remarks>
public sealed class RecurringJob
{
    private readonly List<RecurringJob> _siblings;

    internal RecurringJob(string name, Func<IJob> factory, List<RecurringJob> siblings)
    {
        Name = name;
        Factory = factory;
        _siblings = siblings;
    }

    /// <summary>The durable name this job's last run is recorded under.</summary>
    public string Name { get; private set; }

    /// <summary>When it is due. Null until a cadence step is taken, which the host refuses to start without.</summary>
    public Schedule? Schedule { get; private set; }

    /// <summary>Builds the job to enqueue on each tick. Call it to run one off-schedule.</summary>
    public Func<IJob> Factory { get; }

    /// <summary>Runs it every <paramref name="interval"/>, measured from its last run.</summary>
    public RecurringJob Every(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "A recurring interval must be positive.");
        }

        return With(new EverySchedule(interval));
    }

    /// <summary>Runs it once a day, at the time the next step gives.</summary>
    public DailyJob Daily => new(this);

    /// <summary>Runs it once a week, on the day the next step gives.</summary>
    public WeeklyJob Weekly => new(this);

    /// <summary>Runs it once a month, on the date the next step gives.</summary>
    public MonthlyJob Monthly => new(this);

    /// <summary>Records its runs under <paramref name="name"/> instead of the job's type name.</summary>
    public RecurringJob Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RefuseADuplicateName(name);
        Name = name;
        return this;
    }

    /// <summary>
    /// Refuses a name another schedule already answers to. Two schedules sharing a name share one
    /// <see cref="RecurringJobState"/> row, so each would keep marking the other as having just run, and
    /// between them they would fire once — the failure that looks like "the second one never runs".
    /// </summary>
    internal void RefuseADuplicateName(string name)
    {
        if (_siblings.Any(r => !ReferenceEquals(r, this) && string.Equals(r.Name, name, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"'{name}' is already scheduled. Two schedules for one job need distinct names: "
                + $"give one of them .Named(\"{name.ToLowerInvariant()}-nightly\").",
                nameof(name));
        }
    }

    internal RecurringJob With(Schedule schedule)
    {
        Schedule = schedule;
        return this;
    }
}

/// <summary>A daily job still being worded: <c>.Daily.At(3, 00)</c>.</summary>
/// <param name="job">The job being scheduled.</param>
public readonly struct DailyJob(RecurringJob job)
{
    /// <summary>Runs it at <paramref name="hour"/>:<paramref name="minute"/>, in the app's time zone.</summary>
    public RecurringJob At(int hour, int minute = 0) => At(Schedules.Time(hour, minute));

    /// <summary>Runs it at <paramref name="time"/>, in the app's time zone.</summary>
    public RecurringJob At(TimeOnly time) => job.With(new CalendarSchedule(Cadence.Daily, time, null, null));
}

/// <summary>A weekly job still being worded: <c>.Weekly.On(DayOfWeek.Monday).At(9, 00)</c>.</summary>
/// <param name="job">The job being scheduled.</param>
public readonly struct WeeklyJob(RecurringJob job)
{
    /// <summary>Runs it on <paramref name="day"/>, at the time the next step gives.</summary>
    public WeeklyDayJob On(DayOfWeek day) => new(job, day);
}

/// <summary>A weekly job with its day chosen: <c>.At(9, 00)</c> finishes it.</summary>
/// <param name="job">The job being scheduled.</param>
/// <param name="day">The weekday it runs on.</param>
public readonly struct WeeklyDayJob(RecurringJob job, DayOfWeek day)
{
    /// <summary>Runs it at <paramref name="hour"/>:<paramref name="minute"/>, in the app's time zone.</summary>
    public RecurringJob At(int hour, int minute = 0) => At(Schedules.Time(hour, minute));

    /// <summary>Runs it at <paramref name="time"/>, in the app's time zone.</summary>
    public RecurringJob At(TimeOnly time) => job.With(new CalendarSchedule(Cadence.Weekly, time, day, null));
}

/// <summary>A monthly job still being worded: <c>.Monthly.On(1).At(6, 00)</c>.</summary>
/// <param name="job">The job being scheduled.</param>
public readonly struct MonthlyJob(RecurringJob job)
{
    /// <summary>
    /// Runs it on the <paramref name="day"/>th of each month. A month too short for it runs on its last
    /// day, so <c>On(31)</c> never skips February.
    /// </summary>
    public MonthlyDayJob On(int day)
    {
        if (day is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, "A day of the month is between 1 and 31.");
        }

        return new MonthlyDayJob(job, day);
    }
}

/// <summary>A monthly job with its date chosen: <c>.At(6, 00)</c> finishes it.</summary>
/// <param name="job">The job being scheduled.</param>
/// <param name="day">The day of the month it runs on.</param>
public readonly struct MonthlyDayJob(RecurringJob job, int day)
{
    /// <summary>Runs it at <paramref name="hour"/>:<paramref name="minute"/>, in the app's time zone.</summary>
    public RecurringJob At(int hour, int minute = 0) => At(Schedules.Time(hour, minute));

    /// <summary>Runs it at <paramref name="time"/>, in the app's time zone.</summary>
    public RecurringJob At(TimeOnly time) => job.With(new CalendarSchedule(Cadence.Monthly, time, null, day));
}

/// <summary>Range checks shared by the calendar steps, so each one reads the same way when it is wrong.</summary>
internal static class Schedules
{
    internal static TimeOnly Time(int hour, int minute)
    {
        if (hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), hour, "An hour of the day is between 0 and 23.");
        }

        if (minute is < 0 or > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), minute, "A minute is between 0 and 59.");
        }

        return new TimeOnly(hour, minute);
    }
}
