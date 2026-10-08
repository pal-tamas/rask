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

    /// <summary>
    ///     Stamps this row's own times, for an entity whose package writes it and knows when.
    /// </summary>
    /// <param name="at">The moment to record (UTC).</param>
    /// <remarks>
    ///     <para>
    ///         <c>CreatedAt</c> and <c>UpdatedAt</c> are normally the framework's: Rask's auditing interceptor
    ///         fills them on save. An entity whose table lives in a package that does NOT own the DbContext
    ///         cannot rely on that — the interceptor may simply not be installed — and a column nobody
    ///         stamped reads as <c>0001-01-01</c>, which is not obviously wrong anywhere it is used. Rask's own
    ///         file store found this the hard way: an unstamped <c>CreatedAt</c> became a
    ///         <c>Last-Modified</c> header and the cutoff of an orphan sweep.
    ///     </para>
    ///     <para>
    ///         So an entity that knows its own creation time says so, and the interceptor fills only what is
    ///         still unset. <c>protected</c>, because forging an audit trail from outside is not a thing an
    ///         application should be able to do.
    ///     </para>
    /// </remarks>
    protected void Stamp(DateTime at)
    {
        if (CreatedAt == default)
        {
            CreatedAt = at;
        }

        UpdatedAt = at;
    }
}
