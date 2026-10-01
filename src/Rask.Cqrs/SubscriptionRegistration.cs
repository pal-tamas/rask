namespace Rask.Cqrs;

/// <summary>
///     What the generator knows about one <see cref="ISubscription{TEvent}" /> record: the events it
///     carries, how it filters them, and the policy that admits it. Public only so generated code can build one; you do
///     not use it directly.
/// </summary>
public sealed class SubscriptionRegistration
{
    /// <summary>Describes one subscription record.</summary>
    /// <param name="eventType">The event the subscription carries.</param>
    /// <param name="matches">Calls the record's own <c>Matches</c>, closed over both concrete types.</param>
    /// <param name="canWatch">Asks the <see cref="IWatchPolicy{TSubscription}" /> in the subscriber's scope.</param>
    public SubscriptionRegistration(
        Type eventType,
        Func<object, IEvent, bool> matches,
        Func<IServiceProvider, object, CancellationToken, Task<bool>> canWatch)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(canWatch);
        EventType = eventType;
        Matches = matches;
        CanWatch = canWatch;
    }

    /// <summary>The event this subscription carries.</summary>
    public Type EventType { get; }

    /// <summary>Whether an event is one this subscription asked for.</summary>
    public Func<object, IEvent, bool> Matches { get; }

    /// <summary>Whether the subscriber in the given scope may open this subscription.</summary>
    public Func<IServiceProvider, object, CancellationToken, Task<bool>> CanWatch { get; }
}
