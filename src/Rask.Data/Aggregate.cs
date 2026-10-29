using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
/// The root of a consistency boundary: the one entity outside code loads, changes and saves, together with
/// everything it holds.
/// </summary>
/// <remarks>
/// <para>
/// Only an aggregate is read and written off its type — <c>Product.Where(…)</c>,
/// <c>Product.Create(model)</c> — and only an aggregate gets a generated form model. What it holds is part
/// of it: a property of another composite type is a value object, stored as columns on the aggregate's row.
/// </para>
/// <para>
/// A table carries only the traits the aggregate implements, in any order: <see cref="ITimestamps" /> (or
/// <see cref="ICreated" /> / <see cref="IUpdated" />), <see cref="IVersioned" />, <see cref="IFormModel" />,
/// <see cref="IDeletable" /> or <see cref="ISoftDelete" />, <see cref="IPerTenant" />, <see cref="IBroadcast" />.
/// The generator reads them at compile time; the members they expose are this base's. Every aggregate has a
/// domain-events buffer that the <see cref="DomainEventInterceptor"/> publishes after the change commits.
/// </para>
/// <example>
/// <code>
/// public sealed class Product : Aggregate&lt;Guid&gt;, ITimestamps, IVersioned, IFormModel, IDeletable
/// {
///     private Product() { }
///
///     public string Name { get; private set; } = "";
///     public Money Price { get; private set; } = new(0m, "EUR");   // value object: Price_Amount, Price_Currency
/// }
/// </code>
/// </example>
/// </remarks>
/// <typeparam name="TId">The key type (e.g. <see cref="Guid"/>, <see cref="int"/>, a strongly-typed id).</typeparam>
public abstract class Aggregate<TId> : Entity<TId>, IAggregate, IHasDomainEvents, IStoredAggregate
{
    // Made on the first Raise: a row that is only read never raises one, and a queue or a list page reads thousands.
    private List<IEvent>? _domainEvents;

    // False while only code has held it: see IStoredAggregate.
    private bool _stored;

    /// <summary>Initializes the base.</summary>
    protected Aggregate()
    {
    }

    /// <summary>
    /// The optimistic-concurrency token: bumped on every save of the aggregate, and compared on the next one, so
    /// a save built from a stale copy is refused with <c>DbUpdateConcurrencyException</c>. A column only on an
    /// aggregate that implements <see cref="IVersioned" />.
    /// </summary>
#pragma warning disable S1144 // EF materializes and bumps the column through the setter
    public int Version { get; private set; }
#pragma warning restore S1144

    /// <summary>
    /// When the aggregate was deleted, in UTC, or <c>null</c>. A delete stamps this instead of removing the row,
    /// and a global query filter hides it from every read; <c>IgnoreQueryFilters()</c> lists it again. A column only
    /// on an aggregate that implements <see cref="ISoftDelete" />.
    /// </summary>
#pragma warning disable S1144 // EF materializes the column through the setter; SoftDeleteInterceptor stamps it
    public DateTime? DeletedAt { get; private set; }
#pragma warning restore S1144

    /// <inheritdoc/>
    public IReadOnlyList<IEvent> DomainEvents => _domainEvents ?? (IReadOnlyList<IEvent>)[];

    /// <summary>Records a domain event to be published after the aggregate's change commits.</summary>
    protected void Raise(IEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        (_domainEvents ??= []).Add(domainEvent);
    }

    /// <inheritdoc/>
    public void ClearDomainEvents() => _domainEvents?.Clear();

    bool IStoredAggregate.IsStored => _stored;

    void IStoredAggregate.MarkStored() => _stored = true;
}
