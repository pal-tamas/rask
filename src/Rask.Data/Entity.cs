using System.ComponentModel;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
/// An object with an identity, persisted with Entity Framework Core: the base of <see cref="Aggregate{TId}" />,
/// and of every child entity an aggregate holds.
/// </summary>
/// <remarks>
/// <para>
/// An entity always has an <see cref="Id" />. The entity assigns it where it is built — a <see cref="Guid" /> key
/// is <c>Guid.CreateVersion7()</c> in its constructor or factory — except an integer key, which the database
/// produces on insert.
/// </para>
/// <para>
/// Derive an aggregate root from <see cref="Aggregate{TId}" />. Derive from <see cref="Entity{TId}" /> directly
/// for a child the aggregate holds, such as the lines of an order.
/// </para>
/// </remarks>
/// <typeparam name="TId">The key type (e.g. <see cref="Guid"/>, <see cref="int"/>, a strongly-typed id).</typeparam>
public abstract class Entity<TId> : IEntity
{
    /// <summary>Initializes the base.</summary>
    protected Entity()
    {
    }

    /// <summary>The entity's identity. Set by the derived type's constructor or factory (or EF on materialization).</summary>
    public TId Id { get; protected set; } = default!;

    /// <summary>When the row was inserted, in UTC. Stamped by the framework on insert.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>When the row last changed, in UTC. Stamped by the framework on every insert and update.</summary>
    public DateTime UpdatedAt { get; private set; }
}

/// <summary>
/// The root of a consistency boundary: the one entity outside code loads, changes and saves, together with
/// everything it holds.
/// </summary>
/// <remarks>
/// <para>
/// Only an aggregate is read and written off its type — <c>Product.Where(…)</c>, <c>Product.FindAsync(id)</c>,
/// <c>Product.CreateAsync(model)</c> — and only an aggregate gets a generated form model. What it holds is part
/// of it: a property of another composite type is a value object, stored as columns on the aggregate's row.
/// </para>
/// <para>
/// Every aggregate has, with nothing to declare: <c>CreatedAt</c>/<c>UpdatedAt</c> (from
/// <see cref="Entity{TId}" />), a <see cref="Version" /> concurrency token, soft delete through
/// <see cref="DeletedAt" />, and a domain-events buffer that the <see cref="DomainEventInterceptor"/> publishes
/// after the change commits. The framework writes all of them through EF's change tracker.
/// </para>
/// <example>
/// <code>
/// public sealed class Product : Aggregate&lt;Guid&gt;
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
public abstract class Aggregate<TId> : Entity<TId>, IAggregate, IHasDomainEvents
{
    private readonly List<INotification> _domainEvents = [];

    /// <summary>Initializes the base.</summary>
    protected Aggregate()
    {
    }

    /// <summary>
    /// The optimistic-concurrency token: bumped on every save of the aggregate, and compared on the next one, so
    /// a save built from a stale copy is refused with <c>DbUpdateConcurrencyException</c>.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// When the aggregate was deleted, in UTC, or <c>null</c>. A delete stamps this instead of removing the row,
    /// and a global query filter hides it from every read; <c>IgnoreQueryFilters()</c> lists it again.
    /// </summary>
    public DateTime? DeletedAt { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<INotification> DomainEvents => _domainEvents;

    /// <summary>Records a domain event to be published after the aggregate's change commits.</summary>
    protected void Raise(INotification domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <inheritdoc/>
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>What every <see cref="Entity{TId}" /> is, for code that is generic over entities of any key type.</summary>
/// <remarks>
/// Not a type to implement: derive from <see cref="Entity{TId}" /> or <see cref="Aggregate{TId}" />. It exists
/// because C# cannot infer <c>TId</c> from <c>Product.Where(…)</c>, so the generic surface needs something
/// non-generic to constrain on.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IEntity;

/// <summary>What every <see cref="Aggregate{TId}" /> is, for code that is generic over aggregates of any key type.</summary>
/// <remarks>Not a type to implement: derive from <see cref="Aggregate{TId}" />. See <see cref="IEntity" />.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IAggregate : IEntity;
