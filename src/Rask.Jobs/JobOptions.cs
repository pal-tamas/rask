namespace Rask.Jobs;

/// <summary>Options for the <see cref="JobProcessor{TContext}"/>.</summary>
public sealed class JobOptions
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

    /// <summary>The registered interval-recurring jobs.</summary>
    internal List<RecurringJobDefinition> Recurring { get; } = [];

    /// <summary>
    /// The registered interval-recurring jobs, in registration order — the schedule an operator or an ops
    /// dashboard reads to answer "what is supposed to run, and how often?". Pair each entry with the
    /// <see cref="RecurringJobState"/> row of the same <see cref="RecurringJobDefinition.Name"/> to see when it
    /// last fired, and call <see cref="RecurringJobDefinition.Factory"/> to enqueue an off-schedule run.
    /// </summary>
    public IReadOnlyList<RecurringJobDefinition> RecurringJobs => Recurring;

    /// <summary>Validates the option values (called at registration, so a bad value fails fast rather than tearing down the host later).</summary>
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
    /// Registers an interval-recurring job: the processor enqueues a fresh <typeparamref name="TJob"/>
    /// (from <paramref name="factory"/>) roughly every <paramref name="every"/>, durably tracked by
    /// <paramref name="name"/> so a restart doesn't double-run it.
    /// </summary>
    /// <typeparam name="TJob">The job to enqueue on each tick.</typeparam>
    public JobOptions AddRecurring<TJob>(string name, TimeSpan every, Func<TJob> factory)
        where TJob : IJob
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        if (every <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(every), every, "A recurring interval must be positive.");
        }

        if (Recurring.Any(r => string.Equals(r.Name, name, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A recurring job named '{name}' is already registered.", nameof(name));
        }

        Recurring.Add(new RecurringJobDefinition(name, every, () => factory()));
        return this;
    }
}

/// <summary>A registered interval-recurring job: its durable name, cadence, and a factory for each run.</summary>
/// <param name="Name">The durable name, matching the <see cref="RecurringJobState.Name"/> that tracks its last run.</param>
/// <param name="Interval">How often the processor enqueues a fresh instance.</param>
/// <param name="Factory">Builds the job to enqueue on each tick. Call it to run one off-schedule.</param>
public sealed record RecurringJobDefinition(string Name, TimeSpan Interval, Func<IJob> Factory);
