using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
/// The base class for a domain entity persisted with Entity Framework Core. It owns the identity
/// (<see cref="Id"/>), the audit stamps (<see cref="CreatedAt"/>/<see cref="UpdatedAt"/>, maintained by the
/// <see cref="AuditingInterceptor"/>), and a domain-events buffer that the <see cref="DomainEventInterceptor"/>
/// publishes after the change commits. Opt into soft delete or optimistic concurrency by also implementing
/// <see cref="ISoftDeletable"/> / <see cref="IVersioned"/> on the derived type.
/// </summary>
/// <typeparam name="TId">The key type (e.g. <see cref="Guid"/>, <see cref="int"/>, <see cref="long"/>).</typeparam>
public abstract class Entity<TId> : ITimestamped, IHasDomainEvents
{
    private readonly List<INotification> _domainEvents = [];

    /// <summary>The entity's identity. Set by the derived type's factory (or EF on materialization).</summary>
    public TId Id { get; protected set; } = default!;

    /// <inheritdoc/>
    public DateTime CreatedAt { get; protected set; }

    /// <inheritdoc/>
    public DateTime UpdatedAt { get; protected set; }

    /// <inheritdoc/>
    public IReadOnlyList<INotification> DomainEvents => _domainEvents;

    /// <summary>Records a domain event to be published after the entity's change commits.</summary>
    protected void Raise(INotification domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <inheritdoc/>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
