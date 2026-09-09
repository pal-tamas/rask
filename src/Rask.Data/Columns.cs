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
    /// <summary>When the row was first persisted. Added by <see cref="ITimestamped" />.</summary>
    public const string CreatedAt = "CreatedAt";

    /// <summary>When the row was last persisted. Added by <see cref="ITimestamped" />.</summary>
    public const string UpdatedAt = "UpdatedAt";

    /// <summary>When the row was soft-deleted, or <c>null</c> while it is live. Added by <see cref="ISoftDeletable" />.</summary>
    public const string DeletedAt = "DeletedAt";

    /// <summary>The optimistic-concurrency token. Added by <see cref="IVersioned" />, which must declare it.</summary>
    public const string Version = "Version";
}
