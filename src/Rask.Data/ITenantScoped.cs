namespace Rask.Data;

/// <summary>
///     A <c>DbContext</c> that knows which tenant it is reading for.
/// </summary>
/// <remarks>
///     <para>
///         Implement it on the application's context — <c>: DbContext, ITenantScoped</c> — and pass the
///         context to <c>modelBuilder.ApplyRaskConventions(this)</c>. Nothing needs writing: the default
///         implementation reads <c>Current.Tenant</c>.
///     </para>
///     <para>
///         <b>Why the filter goes through an instance member rather than reading the ambient directly.</b>
///         A query filter is compiled into the model, and the model is CACHED. A static read is evaluated
///         once and inlined into the SQL as a literal, so the first tenant to run a query pins that value for
///         every tenant afterwards — measured, not assumed. Reaching the same value through the context
///         instance makes EF Core lift it to a real parameter and re-bind it per query.
///     </para>
/// </remarks>
public interface ITenantScoped
{
    /// <summary>
    ///     What the tenant filter compares against: the tenant in flight, or <see langword="null" /> inside
    ///     <see cref="Tenant.Across" /> to mean "do not restrict".
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Reading this with no tenant set and no <see cref="Tenant.Across" /> open THROWS, and the throw
    ///         lands where it should: EF Core evaluates this per query, so the exception surfaces at the call
    ///         that tried to read rather than at startup.
    ///     </para>
    ///     <para>
    ///         Null therefore never means "the rows whose TenantId is null" — the filter pairs it with a
    ///         <c>current == null ||</c> guard, so null lifts the restriction instead of narrowing to unowned
    ///         rows. That distinction is the whole difference between <c>Tenant.Across()</c> working and
    ///         silently returning nothing.
    ///     </para>
    ///     <para>
    ///         In an app that registered a resolver (<c>AddRaskTenant</c>) which named no tenant, this is
    ///         <see cref="Guid.AllBitsSet" /> rather than a throw: a value no row holds, which Rask's filter
    ///         reads as "match nothing". A numbered tenant arrives as the <see cref="Guid" /> that carries it,
    ///         and the filter of a table that keeps a number compares the number.
    ///     </para>
    /// </remarks>
    Guid? CurrentTenant => Tenant.Resolve();
}
