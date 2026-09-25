namespace Rask.Cqrs;

/// <summary>
/// Handles an <see cref="INotification"/>. Any number of handlers may exist for one notification;
/// all of them run when it is published.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>Reacts to the published notification. Cancelled with the work that published it.</summary>
    Task Handle(TNotification notification);
}
