using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
/// An entity that records when it was created and last changed. The <see cref="AuditingInterceptor"/>
/// stamps <see cref="CreatedAt"/> on insert and <see cref="UpdatedAt"/> on every insert/update, so the
/// application never sets them by hand. <see cref="Entity{TId}"/> implements this for free.
/// </summary>
public interface ITimestamped
{
    /// <summary>When the row was first persisted (UTC).</summary>
    DateTime CreatedAt { get; }

    /// <summary>When the row was last persisted (UTC).</summary>
    DateTime UpdatedAt { get; }
}

/// <summary>
/// An entity that is soft-deleted rather than physically removed. The <see cref="SoftDeleteInterceptor"/>
/// turns a <c>Remove</c> into a <see cref="DeletedAt"/> stamp, and <see cref="ModelBuilderExtensions.ApplyRaskConventions"/>
/// adds a global query filter (<c>DeletedAt == null</c>) so deleted rows disappear from ordinary queries.
/// Use <c>IgnoreQueryFilters()</c> to see or restore them.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>When the row was soft-deleted (UTC), or <c>null</c> while it is live.</summary>
    DateTime? DeletedAt { get; }
}

/// <summary>
/// An entity guarded by an optimistic-concurrency token. <see cref="ModelBuilderExtensions.ApplyRaskConventions"/>
/// marks <see cref="Version"/> as the concurrency token and the <see cref="AuditingInterceptor"/> bumps it on
/// every update, so a save against a stale version throws <c>DbUpdateConcurrencyException</c>.
/// </summary>
public interface IVersioned
{
    /// <summary>A monotonically increasing revision used as the EF Core concurrency token.</summary>
    int Version { get; }
}

/// <summary>
/// An entity that records domain events for the <see cref="DomainEventInterceptor"/> to publish (via
/// <c>Rask.Cqrs</c>) after the change commits. <see cref="Entity{TId}"/> implements this for free.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>The events raised since the entity was loaded, in the order they were raised.</summary>
    IReadOnlyList<INotification> DomainEvents { get; }

    /// <summary>Clears the recorded events (called by the interceptor after they are published).</summary>
    void ClearDomainEvents();
}
