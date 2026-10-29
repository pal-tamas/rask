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
/// <para>
/// Two entities are equal when they are of the same type and have the same <see cref="Id" />, however they were
/// loaded. One with no id yet — an integer key before its insert — is equal only to itself.
/// </para>
/// </remarks>
/// <typeparam name="TId">The key type (e.g. <see cref="Guid"/>, <see cref="int"/>, a strongly-typed id).</typeparam>
#pragma warning disable S4035 // a base by design: Equals compares GetType(), so a derived type never equals its base
public abstract class Entity<TId> : IEntity, IEquatable<Entity<TId>>
#pragma warning restore S4035
{
    // Taken once: an integer key is assigned on insert, and a set holding the entity must still find it after.
    private int? _hash;

    /// <summary>Initializes the base.</summary>
    protected Entity()
    {
    }

    /// <summary>Equal when of the same type with the same <see cref="Id" />; see <see cref="Equals(Entity{TId})" />.</summary>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    /// <summary>Not equal: see <see cref="op_Equality" />.</summary>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);

    /// <summary>The entity's identity. Set by the derived type's constructor or factory (or EF on materialization).</summary>
    public TId Id { get; protected set; } = default!;

    /// <summary>
    ///     When the row was inserted, in UTC. Stamped by the framework on insert, and a column only on an entity that
    ///     implements <see cref="ICreated" /> or <see cref="ITimestamps" />.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    ///     When the row last changed, in UTC. Stamped by the framework on every insert and update, and a column only on
    ///     an entity that implements <see cref="IUpdated" /> or <see cref="ITimestamps" />.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    private bool IsTransient => EqualityComparer<TId>.Default.Equals(Id, default!);

    /// <summary>
    ///     The same entity: the same type and the same <see cref="Id" />. One with no id yet is equal only to itself.
    /// </summary>
    /// <param name="other">The entity to compare with.</param>
    public bool Equals(Entity<TId>? other) =>
        other is not null &&
        (ReferenceEquals(this, other) ||
         (other.GetType() == GetType() && !IsTransient && !other.IsTransient &&
          EqualityComparer<TId>.Default.Equals(Id, other.Id)));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    /// <inheritdoc />
    /// <remarks>
    ///     Fixed the first time it is asked for: from the type and the id, or — before the entity has an id — from the
    ///     instance, so the id an insert assigns does not move it inside a set.
    /// </remarks>
#pragma warning disable S2328 // taken once on purpose: an insert assigns the id, and a set must still find the entity
    public override int GetHashCode() =>
        _hash ??= IsTransient ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this) : HashCode.Combine(GetType(), Id);
#pragma warning restore S2328

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
