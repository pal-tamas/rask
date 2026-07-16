namespace Rask.Jobs;

/// <summary>Options for the <see cref="JobProcessor{TContext}"/>.</summary>
public sealed class JobOptions
{
    /// <summary>How often the processor polls the jobs table for due work. Default 5s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many jobs to run per poll. Default 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>How many times to attempt a failing job before it is left as a dead letter (kept for inspection). Default 25.</summary>
    public int MaxAttempts { get; set; } = 25;

    /// <summary>The base delay before the first retry; each further retry doubles it (capped at <see cref="MaxRetryDelay"/>). Default 10s.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The cap on the exponential retry backoff. Default 1h.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long completed jobs are kept before being purged. <see cref="TimeSpan.Zero"/> keeps them forever. Default 7 days.</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The registered interval-recurring jobs.</summary>
    internal List<RecurringJobDefinition> Recurring { get; } = [];

    /// <summary>Validates the option values (called at registration, so a bad value fails fast rather than tearing down the host later).</summary>
    internal void Validate()
    {
        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PollInterval), PollInterval, "PollInterval must be positive.");
        }

        if (BatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchSize), BatchSize, "BatchSize must be at least 1.");
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
internal sealed record RecurringJobDefinition(string Name, TimeSpan Interval, Func<IJob> Factory);
