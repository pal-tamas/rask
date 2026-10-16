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
/// A table carries only what its base names, in any order: <see cref="Timestamps" /> (or <see cref="Created" /> /
/// <see cref="Updated" />), <see cref="Versioned" />, <see cref="Model" />, <see cref="Deletable" /> or
/// <see cref="SoftDelete" />, <see cref="PerTenant" />, <see cref="Broadcast" />. Every aggregate has a
/// domain-events buffer that the <see cref="DomainEventInterceptor"/> publishes after the change commits.
/// </para>
/// <example>
/// <code>
/// public sealed class Product : Aggregate&lt;Guid, Timestamps, Versioned, Model, Deletable&gt;
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
    // Made on the first Raise: a row that is only read never raises one, and a queue or a list page reads thousands.
    private List<IEvent>? _domainEvents;

    /// <summary>Initializes the base.</summary>
    protected Aggregate()
    {
    }

    /// <summary>
    /// The optimistic-concurrency token: bumped on every save of the aggregate, and compared on the next one, so
    /// a save built from a stale copy is refused with <c>DbUpdateConcurrencyException</c>.
    /// </summary>
#pragma warning disable S1144 // EF materializes and bumps the column through the setter
    public int Version { get; private set; }
#pragma warning restore S1144

    /// <summary>
    /// When the aggregate was deleted, in UTC, or <c>null</c>. A delete stamps this instead of removing the row,
    /// and a global query filter hides it from every read; <c>IgnoreQueryFilters()</c> lists it again.
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
}

/// <summary>An aggregate that asks for one trait, in any order — see <see cref="IAggregateTrait" />.</summary>
/// <typeparam name="TId">The key type.</typeparam>
/// <typeparam name="T1">A trait.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2326", Justification = "The type arguments ARE the declaration, read by the generator.")]
public abstract class Aggregate<TId, T1> : Aggregate<TId>
    where T1 : IAggregateTrait
{
    /// <summary>Initializes the base.</summary>
    protected Aggregate()
    {
    }
}

/// <summary>An aggregate that asks for 2 traits, in any order — see <see cref="IAggregateTrait" />.</summary>
/// <typeparam name="TId">The key type.</typeparam>
/// <typeparam name="T1">A trait.</typeparam>
/// <typeparam name="T2">A trait.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2326", Justification = "The type arguments ARE the declaration, read by the generator.")]
public abstract class Aggregate<TId, T1, T2> : Aggregate<TId>
    where T1 : IAggregateTrait
    where T2 : IAggregateTrait
{
    /// <summary>Initializes the base.</summary>
    protected Aggregate()
    {
    }
}

/// <summary>An aggregate that asks for 3 traits, in any order — see <see cref="IAggregateTrait" />.</summary>
/// <typeparam name="TId">The key type.</typeparam>
/// <typeparam name="T1">A trait.</typeparam>
/// <typeparam name="T2">A trait.</typeparam>
/// <typeparam name="T3">A trait.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2326", Justification = "The type arguments ARE the declaration, read by the generator.")]
public abstract class Aggregate<TId, T1, T2, T3> : Aggregate<TId>
    where T1 : IAggregateTrait
    where T2 : IAggregateTrait
    where T3 : IAggregateTrait
{
    /// <summary>Initializes the base.</summary>
    protected Aggregate()
    {
    }
}

/// <summary>An aggregate that asks for 4 traits, in any order — see <see cref="IAggregateTrait" />.</summary>
/// <typeparam name="TId">The key type.</typeparam>
/// <typeparam name="T1">A trait.</typeparam>
/// <typeparam name="T2">A trait.</typeparam>
/// <typeparam name="T3">A trait.</typeparam>
/// <typeparam name="T4">A trait.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2326", Justification = "The type arguments ARE the declaration, read by the generator.")]
public abstract class Aggregate<TId, T1, T2, T3, T4> : Aggregate<TId>
    where T1 : IAggregateTrait
    where T2 : IAggregateTrait
    where T3 : IAggregateTrait
    where T4 : IAggregateTrait
{
    /// <summary>Initializes the base.</summary>
    protected Aggregate()
    {
    }
}

/// <summary>An aggregate that asks for 5 traits, in any order — see <see cref="IAggregateTrait" />.</summary>
/// <typeparam name="TId">The key type.</typeparam>
/// <typeparam name="T1">A trait.</typeparam>
/// <typeparam name="T2">A trait.</typeparam>
/// <typeparam name="T3">A trait.</typeparam>
/// <typeparam name="T4">A trait.</typeparam>
/// <typeparam name="T5">A trait.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2326", Justification = "The type arguments ARE the declaration, read by the generator.")]
public abstract class Aggregate<TId, T1, T2, T3, T4, T5> : Aggregate<TId>
    where T1 : IAggregateTrait
    where T2 : IAggregateTrait
    where T3 : IAggregateTrait
    where T4 : IAggregateTrait
    where T5 : IAggregateTrait
{
    /// <summary>Initializes the base.</summary>
    protected Aggregate()
    {
    }
}

/// <summary>An aggregate that asks for 6 traits, in any order — see <see cref="IAggregateTrait" />.</summary>
/// <typeparam name="TId">The key type.</typeparam>
/// <typeparam name="T1">A trait.</typeparam>
/// <typeparam name="T2">A trait.</typeparam>
/// <typeparam name="T3">A trait.</typeparam>
/// <typeparam name="T4">A trait.</typeparam>
/// <typeparam name="T5">A trait.</typeparam>
/// <typeparam name="T6">A trait.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2326", Justification = "The type arguments ARE the declaration, read by the generator.")]
public abstract class Aggregate<TId, T1, T2, T3, T4, T5, T6> : Aggregate<TId>
    where T1 : IAggregateTrait
    where T2 : IAggregateTrait
    where T3 : IAggregateTrait
    where T4 : IAggregateTrait
    where T5 : IAggregateTrait
    where T6 : IAggregateTrait
{
    /// <summary>Initializes the base.</summary>
    protected Aggregate()
    {
    }
}
