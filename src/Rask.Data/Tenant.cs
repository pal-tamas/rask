namespace Rask.Data;

/// <summary>
///     Which tenant the work in flight belongs to.
/// </summary>
/// <remarks>
///     <para>
///         Every read and write on a <see cref="Tenancy.PerTenant" /> table is filtered by this. It is
///         ambient rather than a parameter because a Rask read is a static call that opens its own context —
///         <c>Invoice.Read.Where(…)</c> — so there is nowhere to pass it; and it flows on
///         <see cref="AsyncLocal{T}" />, so it follows an await without being handed on.
///     </para>
///     <para>
///         <b>A tenant-scoped read with no tenant set THROWS.</b> Returning nothing would be safe against
///         leaks and indistinguishable from an empty database, which is the failure this framework keeps
///         getting bitten by; returning everything would be the leak itself. A background job therefore
///         opens the scope its row recorded, and anything that genuinely spans tenants says
///         <see cref="Across" /> out loud.
///     </para>
/// </remarks>
public static class Tenant
{
    private static readonly AsyncLocal<State> Ambient = new();

    /// <summary>The tenant in flight, or <see langword="null" /> when none is set.</summary>
    public static Guid? Current => Ambient.Value.Tenant;

    /// <summary>Whether the work in flight deliberately spans tenants — see <see cref="Across" />.</summary>
    public static bool IsAcrossTenants => Ambient.Value.Across;

    /// <summary>
    ///     The tenant in flight, or a thrown exception when there is none.
    /// </summary>
    /// <exception cref="InvalidOperationException">No tenant is set and <see cref="Across" /> is not open.</exception>
    public static Guid Required =>
        Current ?? throw new InvalidOperationException(
            "No tenant is set, so a tenant-scoped table cannot be read or written. Open one with " +
            "Tenant.Use(id) — a background job uses the tenant recorded on its own row — or say " +
            "Tenant.Across() when the work deliberately spans tenants.");

    /// <summary>
    ///     Makes <paramref name="tenant" /> the tenant until the returned scope is disposed.
    /// </summary>
    /// <param name="tenant">The tenant to work in.</param>
    /// <returns>A scope that restores the previous tenant.</returns>
    public static IDisposable Use(Guid tenant) => new Scope(new State(tenant, Across: false));

    /// <summary>
    ///     Lets the work in flight see every tenant, until the returned scope is disposed.
    /// </summary>
    /// <returns>A scope that restores the previous tenant.</returns>
    /// <remarks>
    ///     The one deliberate way across a tenant boundary. <c>IgnoreQueryFilters()</c> is NOT: it means
    ///     "include soft-deleted rows" and leaves the tenant filter exactly where it is, so an existing call
    ///     never quietly becomes a cross-tenant read. This is a block rather than a per-query call so that
    ///     crossing a boundary is one greppable thing a reviewer can find.
    /// </remarks>
    public static IDisposable Across() => new Scope(new State(Tenant: null, Across: true));

    /// <summary>Clears the tenant for the duration of the returned scope.</summary>
    /// <returns>A scope that restores the previous tenant.</returns>
    /// <remarks>For a test, or for the sign-in that has to find a user before it can know their tenant.</remarks>
    public static IDisposable None() => new Scope(new State(Tenant: null, Across: false));

    private readonly record struct State(Guid? Tenant, bool Across);

    private sealed class Scope : IDisposable
    {
        private readonly State _previous;
        private bool _disposed;

        internal Scope(State state)
        {
            _previous = Ambient.Value;
            Ambient.Value = state;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Ambient.Value = _previous;
            }
        }
    }
}

/// <summary>
///     A <c>DbContext</c> that knows which tenant it is reading for.
/// </summary>
/// <remarks>
///     <para>
///         Implement it on the application's context — <c>: DbContext, ITenantScoped</c> — and pass the
///         context to <c>modelBuilder.ApplyRaskConventions(this)</c>. Nothing needs writing: the default
///         implementation reads <see cref="Tenant.Current" />.
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
    /// </remarks>
    Guid? CurrentTenant => Tenant.IsAcrossTenants ? null : Tenant.Required;
}
