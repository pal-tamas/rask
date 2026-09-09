using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
/// An entity that records when it was created and last changed. The <see cref="AuditingInterceptor"/>
/// stamps <c>CreatedAt</c> on insert and <c>UpdatedAt</c> on every insert and update, so the application
/// never sets them by hand.
/// </summary>
/// <remarks>
/// <para>
/// <b>A marker, and nothing more — the columns do not have to appear on your class.</b>
/// <see cref="ModelBuilderExtensions.ApplyRaskConventions"/> adds them as EF shadow properties, so the
/// domain model stays free of infrastructure it never reads:
/// </para>
/// <code>
/// public sealed class Product : Model&lt;Guid&gt;, ITimestamped
/// {
///     public string Name { get; private set; } = "";   // and that is the whole class
/// }
/// </code>
/// <para>
/// Declaring the property is how you opt into <em>reading</em> it — when a screen shows "added on", or a
/// query orders by it:
/// </para>
/// <code>
/// public sealed class Product : Model&lt;Guid&gt;, ITimestamped
/// {
///     public DateTime CreatedAt { get; private set; }   // now selectable, filterable, renderable
/// }
/// </code>
/// <para>
/// One or both, in any combination — declare only <c>CreatedAt</c> and <c>UpdatedAt</c> stays a shadow
/// column. A private setter is enough either way: the framework writes these through the change tracker,
/// not through the CLR setter.
/// </para>
/// </remarks>
public interface ITimestamped;

/// <summary>
/// An entity that is soft-deleted rather than physically removed. The <see cref="SoftDeleteInterceptor"/>
/// turns a <c>Remove</c> into a <c>DeletedAt</c> stamp, and
/// <see cref="ModelBuilderExtensions.ApplyRaskConventions"/> adds a global query filter
/// (<c>DeletedAt == null</c>) so deleted rows disappear from ordinary queries. Use
/// <c>IgnoreQueryFilters()</c> to see or restore them.
/// </summary>
/// <remarks>
/// A marker: <c>DeletedAt</c> is added as a shadow column unless you declare
/// <c>public DateTime? DeletedAt { get; private set; }</c> yourself, which is worth doing when the
/// application shows or clears it — a "restore" screen wants to read it.
/// </remarks>
public interface ISoftDeletable;

/// <summary>
/// An entity guarded by an optimistic-concurrency token.
/// <see cref="ModelBuilderExtensions.ApplyRaskConventions"/> marks <c>Version</c> as the concurrency
/// token and the <see cref="AuditingInterceptor"/> bumps it on every update, so a save against a stale
/// version throws <c>DbUpdateConcurrencyException</c>.
/// </summary>
/// <remarks>
/// <b>The one marker that must declare its property</b>: add
/// <c>public int Version { get; private set; }</c> to the model. Optimistic concurrency exists to
/// round-trip the token through an edit form — so that an edit which started before someone else's save
/// is rejected rather than silently overwriting it — and a value the application cannot read is one it
/// cannot send back. A shadow column is refused while the model is built, by name.
/// </remarks>
public interface IVersioned;

/// <summary>
/// An entity that records domain events for the <see cref="DomainEventInterceptor"/> to publish (via
/// <c>Rask.Cqrs</c>) after the change commits. <see cref="Model{TId}"/> implements this for free.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>The events raised since the entity was loaded, in the order they were raised.</summary>
    IReadOnlyList<INotification> DomainEvents { get; }

    /// <summary>Clears the recorded events (called by the interceptor after they are published).</summary>
    void ClearDomainEvents();
}
