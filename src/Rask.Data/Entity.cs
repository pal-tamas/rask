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
    ///     Which tenant owns this row, on a table whose <c>Scope</c> const says
    ///     <see cref="Tenancy.PerTenant" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Stamped from <c>Current.Tenant</c> on insert and refused thereafter: a row does not
    ///         move between tenants. It is never on the generated form model, so a post cannot set it.
    ///     </para>
    ///     <para>
    ///         Declared here rather than on <see cref="Aggregate{TId}" /> so a CHILD carries it too. A child
    ///         is queryable through its own read face, which would otherwise return every tenant&apos;s rows.
    ///         On a table that is not tenant-scoped the column is ignored, exactly as <c>DeletedAt</c> and
    ///         <c>Version</c> are when their consts decline them.
    ///     </para>
    /// </remarks>
    public Guid? TenantId { get; private set; }

    /// <summary>Records which tenant owns this row. Called by the framework on insert.</summary>
    /// <param name="tenant">The owning tenant.</param>
    internal void AssignTenant(Guid tenant) => TenantId = tenant;

    /// <summary>
    ///     Records which tenant this row belongs to, for an entity whose package writes it.
    /// </summary>
    /// <param name="tenant">The owning tenant, or <see langword="null" /> for a row that belongs to nobody.</param>
    /// <remarks>
    ///     <para>
    ///         The sibling of <see cref="Stamp" />, and it exists for the same tables: a queue's rows carry
    ///         the tenant they were enqueued for, but the queue itself is NOT partitioned — a drain has to see
    ///         every tenant's work, so these tables must not take the query filter that a
    ///         <see cref="Tenancy.PerTenant" /> table takes. They record the tenant as data rather than as a
    ///         partition, and the runner re-enters it before invoking the handler.
    ///     </para>
    ///     <para>
    ///         Null is an ordinary answer here: a job enqueued by the host at startup belongs to no tenant.
    ///     </para>
    /// </remarks>
    protected void RecordTenant(Guid? tenant) => TenantId = tenant;

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
