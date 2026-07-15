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
}
