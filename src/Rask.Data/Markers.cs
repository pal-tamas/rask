using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
/// An entity that records domain events for the <see cref="DomainEventInterceptor"/> to publish (via
/// <c>Rask.Cqrs</c>) after the change commits. <see cref="Aggregate{TId}"/> implements this for free.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>The events raised since the entity was loaded, in the order they were raised.</summary>
    IReadOnlyList<INotification> DomainEvents { get; }

    /// <summary>Clears the recorded events (called by the interceptor after they are published).</summary>
    void ClearDomainEvents();
}
