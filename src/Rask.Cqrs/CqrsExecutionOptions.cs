namespace Rask.Cqrs;

/// <summary>The runtime snapshot of the event-fan-out knobs from <see cref="CqrsOptions"/>.</summary>
internal sealed class CqrsExecutionOptions
{
    internal static readonly CqrsExecutionOptions Default = new();

    public EventPublishStrategy PublishStrategy { get; init; } = EventPublishStrategy.Sequential;

    public bool StopOnFirstException { get; init; } = true;

    public int ReplayCapacity { get; init; } = 4096;

    public int SubscriptionBuffer { get; init; } = 256;

    public TimeSpan SubscriptionReconnectDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan SubscriptionReconnectCeiling { get; init; } = TimeSpan.FromSeconds(30);
}
