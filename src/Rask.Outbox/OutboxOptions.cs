namespace Rask.Outbox;

/// <summary>Options for the <see cref="OutboxProcessor{TContext}"/>.</summary>
public sealed class OutboxOptions
{
    /// <summary>How often the processor polls the outbox table for unpublished messages. Default 5s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many messages to publish per poll. Default 100.</summary>
    public int BatchSize { get; set; } = 100;

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
}
