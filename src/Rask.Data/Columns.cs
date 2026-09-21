namespace Rask.Data;

/// <summary>
///     The names of the columns Rask's marker interfaces add.
/// </summary>
/// <remarks>
///     <para>
///         These used to be properties on the interfaces, and reading one was <c>entity.CreatedAt</c>. A
///         marker no longer forces the property onto the model — the column can be an EF shadow property
///         — so the name is the thing to hold on to, and holding it here keeps it out of string literals
///         scattered across the framework and your app:
///     </para>
///     <example>
///         <code>
/// var newest = await Product.All.QueryAsync((q, ct) =&gt; q
///     .OrderByDescending(p =&gt; EF.Property&lt;DateTime&gt;(p, Columns.CreatedAt))
///     .Take(10)
///     .ToListAsync(ct));
///         </code>
///     </example>
///     <para>
///         A model that declares the property does not need any of this — <c>p.CreatedAt</c> is an
///         ordinary property then, and that is the reason to declare one.
///     </para>
/// </remarks>
public static class Columns
{
    /// <summary>When the row was first persisted. Added by <see cref="Entity{TId}" />.</summary>
    public const string CreatedAt = "CreatedAt";

    /// <summary>When the row was last persisted. Added by <see cref="Entity{TId}" />.</summary>
    public const string UpdatedAt = "UpdatedAt";

    /// <summary>When the row was soft-deleted, or <c>null</c> while it is live. Added by <see cref="Aggregate{TId}" />.</summary>
    public const string DeletedAt = "DeletedAt";

    /// <summary>The optimistic-concurrency token. Added by <see cref="Aggregate{TId}" />, which must declare it.</summary>
    public const string Version = "Version";

    /// <summary>Which tenant owns the row. Added only to a table whose <c>Scope</c> const says <c>PerTenant</c>.</summary>
    public const string TenantId = "TenantId";

    /// <summary>
    ///     <see cref="TenantId" /> with its null folded to <see cref="System.Guid.Empty" />, so a unique index
    ///     can include the tenant without depending on how a provider treats NULL.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only a table that maps it gets it, and only one does: the accounts table, whose tenant is
    ///         genuinely optional because an administrator belongs to no tenant. Every other tenant-scoped row
    ///         is stamped from the ambient tenant and can never be null.
    ///     </para>
    ///     <para>
    ///         It exists because NULL in a unique index is not portable. SQLite and PostgreSQL treat two NULLs
    ///         as distinct — so any number of administrators could share one address — while SQL Server treats
    ///         them as equal, so only one could. Same schema, three behaviours. Folding the null away means one
    ///         ordinary unique index that behaves identically everywhere.
    ///     </para>
    /// </remarks>
    public const string TenantKey = "TenantKey";
}
