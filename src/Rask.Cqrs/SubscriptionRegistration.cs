namespace Rask.Cqrs;

/// <summary>
///     What the generator knows about one <see cref="ISubscription{TNotification}" /> record: the notifications it
///     carries, how it filters them, and the policy that admits it. Public only so generated code can build one; you do
///     not use it directly.
/// </summary>
public sealed class SubscriptionRegistration
{
    /// <summary>Describes one subscription record.</summary>
    /// <param name="notificationType">The notification the subscription carries.</param>
    /// <param name="matches">Calls the record's own <c>Matches</c>, closed over both concrete types.</param>
    /// <param name="canWatch">Asks the <see cref="IWatchPolicy{TSubscription}" /> in the subscriber's scope.</param>
    public SubscriptionRegistration(
        Type notificationType,
        Func<object, INotification, bool> matches,
        Func<IServiceProvider, object, CancellationToken, Task<bool>> canWatch)
    {
        ArgumentNullException.ThrowIfNull(notificationType);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(canWatch);
        NotificationType = notificationType;
        Matches = matches;
        CanWatch = canWatch;
    }

    /// <summary>The notification this subscription carries.</summary>
    public Type NotificationType { get; }

    /// <summary>Whether a notification is one this subscription asked for.</summary>
    public Func<object, INotification, bool> Matches { get; }

    /// <summary>Whether the subscriber in the given scope may open this subscription.</summary>
    public Func<IServiceProvider, object, CancellationToken, Task<bool>> CanWatch { get; }
}
