namespace Rask.Data;

/// <summary>Options for <see cref="RaskDataServiceCollectionExtensions.AddRaskData"/>.</summary>
public sealed class RaskDataOptions
{
    /// <summary>
    /// When <c>true</c> (the default), the <see cref="DomainEventInterceptor"/> is registered and domain
    /// events are published in-process after commit. Set to <c>false</c> when a transactional outbox owns
    /// delivery (e.g. <c>Rask.Outbox</c>'s <c>AddRaskOutbox</c>), so events are not delivered twice.
    /// </summary>
    public bool DispatchDomainEventsInProcess { get; set; } = true;
}
