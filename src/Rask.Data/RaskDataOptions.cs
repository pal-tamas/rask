namespace Rask.Data;

/// <summary>Options for <see cref="RaskDataServiceCollectionExtensions.AddRaskData"/>.</summary>
public sealed class RaskDataOptions
{
    /// <summary>
    /// Whether the <see cref="DomainEventInterceptor"/> publishes each entity's domain events in-process
    /// after the change commits.
    /// <list type="bullet">
    /// <item><description>
    /// <c>null</c> (the default) — <b>automatic</b>. Events are published in-process unless something else
    /// has taken ownership of delivery by registering an <see cref="IDomainEventDeliveryOwner"/>, which is
    /// what <c>Rask.Outbox</c>'s <c>AddRaskOutbox</c> does. The decision is made when the container is
    /// built, not when this method is called, so it does not depend on registration order.
    /// </description></item>
    /// <item><description>
    /// <c>true</c> — always publish in-process, <em>even when an outbox owns delivery</em>. Every event is
    /// then delivered twice, and the outbox's own copy is lost besides (see the remarks). Set this only
    /// when you have a specific reason and no outbox.
    /// </description></item>
    /// <item><description>
    /// <c>false</c> — never publish in-process. The interceptor is not registered at all.
    /// </description></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Why the automatic answer matters: <see cref="DomainEventInterceptor"/> <em>drains and clears</em>
    /// each entity's events in <c>SavingChanges</c> — before <c>Rask.Outbox</c>'s interceptor can copy them
    /// into the outbox table. Running both leaves the outbox permanently empty, so delivery silently stops
    /// being durable while nothing fails, because the handlers still run in-process. Leaving this
    /// <c>null</c> makes that combination unreachable.
    /// </remarks>
    public bool? DispatchDomainEventsInProcess { get; set; }
}
