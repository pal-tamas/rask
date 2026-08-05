namespace Rask.Outbox;

/// <summary>Options for the <see cref="OutboxProcessor{TContext}"/>.</summary>
public sealed class OutboxOptions
{
    /// <summary>How often the processor polls the outbox table for unpublished messages. Default 5s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many messages to claim and publish per poll. Default 100, maximum 1000.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How long a claimed message stays invisible to other processor instances. Default 5 minutes.
    /// </summary>
    /// <remarks>
    /// This is the recovery window, not a timeout: nothing cancels a dispatch that overruns it. A processor
    /// that dies mid-dispatch makes its work claimable again after this long, so it must comfortably exceed
    /// the slowest handler you run — set it too low and a slow handler's message is picked up by a second
    /// instance while the first is still running it, which is the duplicate the lease exists to prevent.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How many times to retry a failing message before it is left for inspection. Default 10.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// How long published messages are kept before being purged. <see cref="TimeSpan.Zero"/> (or any
    /// non-positive value) keeps them forever. Default 7 days, matching <c>Rask.Jobs</c> and <c>Rask.Mail</c>.
    /// <para>
    /// Only messages that were successfully published are ever removed. A dead letter has no
    /// <see cref="OutboxMessage.ProcessedAt"/>, so retention never deletes the one row you still need to
    /// look at, whatever value you set here.
    /// </para>
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The ceiling on <see cref="BatchSize"/> — see <see cref="Validate"/> for why there is one.</summary>
    internal const int MaxBatchSize = 1000;

    /// <summary>
    /// How long a message that is already being published may keep publishing after the host is asked to
    /// stop.
    /// <para>
    /// On <c>SIGTERM</c> the processor immediately stops picking up <em>new</em> messages, but the one
    /// already in your handler is given this long to finish rather than being cancelled mid-call.
    /// </para>
    /// <para>
    /// A message that outlives the grace is cancelled, has the attempt its claim counted rolled back, and
    /// is re-published from the top on the next boot. The roll-back is deliberate: a redeploy is not a
    /// failure, and letting it count would march never-failing work toward its dead letter at the cadence
    /// you deploy. Handlers must be idempotent either way — an interrupted message always re-runs whole.
    /// </para>
    /// <para>
    /// Cannot exceed <c>HostOptions.ShutdownTimeout</c>: once that elapses the host stops waiting for
    /// hosted services, so a grace longer than it silently does not happen. Default 5s;
    /// <see cref="TimeSpan.Zero"/> cancels immediately.
    /// </para>
    /// </summary>
    public TimeSpan ShutdownGracePeriod { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Validates the option values. Called from <c>AddRaskOutbox</c>, so a bad value fails fast at
    /// registration rather than throwing out of <c>new PeriodicTimer(...)</c> on the background thread —
    /// which, with the default <c>BackgroundServiceExceptionBehavior.StopHost</c>, takes the host down at
    /// an unrelated moment with an unrelated-looking stack.
    /// </summary>
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

        if (MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAttempts), MaxAttempts, "MaxAttempts must be at least 1.");
        }

        if (RetentionPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RetentionPeriod), RetentionPeriod, "RetentionPeriod cannot be negative.");
        }

        if (ShutdownGracePeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownGracePeriod), ShutdownGracePeriod, "ShutdownGracePeriod cannot be negative (Zero cancels immediately).");
        }

        // CancellationTokenSource.CancelAfter throws above int.MaxValue milliseconds, and it would throw
        // from the shutdown path — the worst place to find out.
        if (ShutdownGracePeriod.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownGracePeriod), ShutdownGracePeriod, $"ShutdownGracePeriod must be at most {TimeSpan.FromMilliseconds(int.MaxValue)}.");
        }

        if (LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(LeaseDuration), LeaseDuration, "LeaseDuration must be positive.");
        }

        if (LeaseDuration <= PollInterval)
        {
            // A lease that expires within one poll guarantees every message is stolen mid-flight by the next
            // instance to look — strictly worse than no lease, so it is refused rather than warned about.
            throw new ArgumentOutOfRangeException(
                nameof(LeaseDuration),
                LeaseDuration,
                $"LeaseDuration must be longer than PollInterval ({PollInterval}), or every claimed message is stolen before it finishes.");
        }
    }
}
