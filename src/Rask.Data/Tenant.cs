namespace Rask.Data;

/// <summary>
///     Which tenant the work in flight belongs to.
/// </summary>
/// <remarks>
///     <para>
///         Every read and write on a <see cref="Tenancy.PerTenant" /> table is filtered by this. It is
///         ambient rather than a parameter because a Rask read is a static call that opens its own context —
///         <c>Invoice.Where(…)</c> — so there is nowhere to pass it; and it flows on
///         <see cref="AsyncLocal{T}" />, so it follows an await without being handed on.
///     </para>
///     <para>
///         This type holds the SCOPES — <see cref="Use(Guid)" />, <see cref="Across" />, <see cref="None" />. Which
///         tenant is in flight is read from <c>Current.Tenant</c>, beside the current user.
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
    /// <summary>
    ///     The claim the signed-in principal carries its tenant on.
    /// </summary>
    /// <remarks>
    ///     Declared here rather than in Rask.Auth because it is the contract between them: the data layer
    ///     filters by it and the auth layer issues it, and neither references the other.
    /// </remarks>
    public const string ClaimType = "rask:tenant";

    private static readonly AsyncLocal<State> Ambient = new();

    /// <summary>Whether the work in flight deliberately spans tenants — see <see cref="Across" />.</summary>
    public static bool IsAcrossTenants => Ambient.Value.AllTenants;

    /// <summary>The tenant an explicit <see cref="Use(Guid)" /> scope set, ignoring the principal.</summary>
    /// <remarks>The tenant in flight from every source is <c>Current.Tenant</c>.</remarks>
    internal static Guid? Explicit => Ambient.Value.Tenant;

    /// <summary>
    ///     Makes <paramref name="tenant" /> the tenant until the returned scope is disposed.
    /// </summary>
    /// <param name="tenant">The tenant to work in.</param>
    /// <returns>A scope that restores the previous tenant.</returns>
    public static IDisposable Use(Guid tenant) => new Scope(new State(tenant, AllTenants: false));

    /// <summary>
    ///     Makes the tenant numbered <paramref name="tenant" /> the tenant until the returned scope is disposed.
    /// </summary>
    /// <param name="tenant">The number of the tenant to work in — the value its rows hold in <c>TenantId</c>.</param>
    /// <returns>A scope that restores the previous tenant.</returns>
    /// <remarks>
    ///     <para>
    ///         For an app whose tenants are rows with an integer key. A table that declares
    ///         <c>public int? TenantId { get; private set; }</c> (or <c>long?</c>) is filtered by, and stamped
    ///         with, this number.
    ///     </para>
    ///     <para>
    ///         Everywhere else the tenant is still a <see cref="Guid" /> — <c>Current.Tenant</c>, a job's row, a
    ///         cache key — and the number travels inside it by a fixed rule: the first eight bytes are zero and
    ///         the last eight are the number, big-endian. <c>Tenant.Use(42)</c> is
    ///         <c>Tenant.Use(new Guid("00000000-0000-0000-0000-00000000002a"))</c>, so a job enqueued in tenant
    ///         42 runs in tenant 42. Tenant <c>0</c> would be <see cref="Guid.Empty" />, which the batteries read
    ///         as "no tenant": number tenants from one.
    ///     </para>
    /// </remarks>
    public static IDisposable Use(long tenant) => Use(TenantNumber.ToGuid(tenant));

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
    public static IDisposable Across() => new Scope(new State(Tenant: null, AllTenants: true));

    /// <summary>Clears the tenant for the duration of the returned scope.</summary>
    /// <returns>A scope that restores the previous tenant.</returns>
    /// <remarks>For a test, or for the sign-in that has to find a user before it can know their tenant.</remarks>
    public static IDisposable None() => new Scope(new State(Tenant: null, AllTenants: false));

    /// <summary>
    ///     What a context's tenant filter compares against: an explicit scope first, then the principal.
    /// </summary>
    /// <returns>The tenant to filter by, or <see langword="null" /> to filter by nothing.</returns>
    /// <remarks>
    ///     <para>
    ///         Order matters. <see cref="Use(Guid)" /> and <see cref="Across" /> win over the principal, because a
    ///         background job runs for the tenant its own row recorded rather than for whoever enqueued it,
    ///         and an admin who has switched tenant is working in the one they chose.
    ///     </para>
    ///     <para>
    ///         Null means "do not restrict", never "the rows nobody owns" — see
    ///         <see cref="ITenantScoped.CurrentTenant" />.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     Nothing says which tenant: no scope is open and the principal carries no tenant.
    /// </exception>
    public static Guid? Resolve() => Ambient.Value.AllTenants ? null : Current.RequiredTenant;

    private readonly record struct State(Guid? Tenant, bool AllTenants);

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
