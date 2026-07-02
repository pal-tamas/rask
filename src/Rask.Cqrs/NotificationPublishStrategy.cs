namespace Rask.Cqrs;

/// <summary>Controls how <see cref="IPublisher.Publish{TNotification}"/> runs a notification's handlers.</summary>
public enum NotificationPublishStrategy
{
    /// <summary>
    /// Run handlers one after another in registration order (the default). With
    /// <see cref="CqrsOptions.StopOnFirstNotificationException"/> set, the first failure stops the
    /// run and is rethrown; otherwise every handler runs and failures are aggregated.
    /// </summary>
    Sequential,

    /// <summary>
    /// Start every handler and await them together with <see cref="Task.WhenAll(Task[])"/>. All
    /// handlers run; the first failure surfaces (the rest are on the returned task's exceptions).
    /// </summary>
    WhenAll,
}
