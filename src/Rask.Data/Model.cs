using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
/// The root of the entity hierarchy, and what the model surface is keyed on: every type that derives from
/// this gains the reads <see cref="ModelSet" /> declares — <c>Product.Where</c>, <c>Product.FindAsync</c>,
/// <c>Product.AsQueryable</c> — and the source generator gives it a <c>ProductModel</c> with the writes
/// that take one (<c>Product.CreateAsync</c>, <c>Product.UpdateAsync</c>, <c>Product.DeleteAsync</c>).
/// </summary>
/// <remarks>
/// Carries no state. It exists so that surface can be constrained to entities rather than to
/// <c>class</c>, which would put <c>Where</c> on every type in the program. Derive from
/// <see cref="Model{TId}" /> instead of this — it is the one that has an identity.
/// </remarks>
public abstract class Model
{
    /// <summary>Initializes the base. Derive from <see cref="Model{TId}" /> unless the key is composite.</summary>
    protected Model()
    {
    }
}

/// <summary>
/// The base class for a domain entity persisted with Entity Framework Core. It owns the identity
/// (<see cref="Id"/>) and a domain-events buffer that the <see cref="DomainEventInterceptor"/> publishes
/// after the change commits.
/// </summary>
/// <remarks>
/// <para>
/// Everything else is opt-in, declared by implementing a marker interface and the property it names —
/// so a model carries the columns it asked for and no others:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ITimestamped"/> — <c>CreatedAt</c> / <c>UpdatedAt</c> audit stamps.</description></item>
/// <item><description><see cref="ISoftDeletable"/> — <c>DeletedAt</c>, and the filter that hides it.</description></item>
/// <item><description><see cref="IVersioned"/> — <c>Version</c>, the optimistic-concurrency token.</description></item>
/// </list>
/// <example>
/// <code>
/// public sealed class Product : Model&lt;Guid&gt;, ITimestamped, ISoftDeletable
/// {
///     public string Name { get; private set; } = "";
///     public DateTime CreatedAt { get; private set; }   // ITimestamped
///     public DateTime UpdatedAt { get; private set; }
///     public DateTime? DeletedAt { get; private set; }  // ISoftDeletable
/// }
/// </code>
/// </example>
/// <para>
/// A private setter is enough for all of them: the framework owns these columns and writes them through
/// EF's change tracker rather than the CLR setter, which is why they are not yours to assign.
/// </para>
/// </remarks>
/// <typeparam name="TId">The key type (e.g. <see cref="Guid"/>, <see cref="int"/>, <see cref="long"/>).</typeparam>
public abstract class Model<TId> : Model, IHasDomainEvents
{
    private readonly List<INotification> _domainEvents = [];

    /// <summary>The entity's identity. Set by the derived type's factory (or EF on materialization).</summary>
    public TId Id { get; protected set; } = default!;

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
