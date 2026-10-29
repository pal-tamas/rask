namespace Rask.Data;

/// <summary>
///     Which tenant the work in flight belongs to.
/// </summary>
/// <remarks>
///     <para>
///         Every read and write on a <see cref="PerTenant" /> table is filtered by this. It is
///         ambient rather than a parameter because a Rask read is a static call that opens its own context —
///         <c>Invoice.Where(…)</c> — so there is nowhere to pass it; and it flows on
///         <see cref="AsyncLocal{T}" />, so it follows an await without being handed on.
///     </para>
///     <para>
///         This type holds the SCOPES — <see cref="Use(Guid)" />, <see cref="Across" />, <see cref="None" />. The
///         tenant in flight is read as the app's own row, <c>Current.Tenant</c>, once its tenant aggregate says
///         <see cref="ITenant" />.
///     </para>
///     <para>
///         <b>A tenant-scoped read with no tenant set THROWS.</b> Returning nothing would be safe against
///         leaks and indistinguishable from an empty database, which is the failure this framework keeps
///         getting bitten by; returning everything would be the leak itself. A background job therefore
///         opens the scope its row recorded, and anything that genuinely spans tenants says
///         <see cref="Across" /> out loud.
///     </para>
///     <para>
///         An app that says where its tenant comes from — <c>services.AddRaskTenant(sp =&gt; …)</c> — has made
///         "no tenant" an ordinary answer instead: a request on a host no tenant owns. There a tenant-scoped
///         table reads as EMPTY and a write to one is refused with a <see cref="MissingTenantException" />.
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

    /// <summary>
    ///     What a filter compares against when a resolver is registered and named no tenant: a value no row can
    ///     hold, so the read matches nothing.
    /// </summary>
    /// <remarks>
    ///     RFC 9562's "Max UUID", which no generator produces and no number embeds as. It never reaches SQL —
    ///     Rask's own filter turns it into "match nothing" — but it is what <see cref="Resolve" /> hands a
    ///     filter an app wrote itself, where returning null would mean "every tenant".
    /// </remarks>
    internal static readonly Guid Nobody = Guid.AllBitsSet;

    private static readonly AsyncLocal<State> Ambient = new();

    /// <summary>Whether the work in flight deliberately spans tenants — see <see cref="Across" />.</summary>
    public static bool IsAcrossTenants => Ambient.Value.AllTenants;

    /// <summary>The tenant an explicit <see cref="Use(Guid)" /> scope set, ignoring the principal.</summary>
    /// <remarks>The tenant in flight from every source is <see cref="Key" />.</remarks>
    internal static Guid? Explicit => Ambient.Value.Tenant;

    /// <summary>
    ///     The tenant the work in flight belongs to, as the <see cref="Guid" /> the framework carries it in, or
    ///     <see langword="null" /> when it belongs to none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An explicit <see cref="Use(Guid)" /> first — a job runs for the tenant its row recorded, an admin in
    ///         the tenant they chose — then the app's resolver (<c>AddRaskTenant</c>), then the
    ///         <see cref="ClaimType" /> claim of <c>Current.Principal</c>.
    ///     </para>
    ///     <para>
    ///         Null inside <see cref="Across" /> — work that deliberately spans tenants is not done on behalf of any
    ///         one of them — and inside <see cref="None" />, which clears it.
    ///     </para>
    ///     <para>
    ///         What the framework's own readers use: a job's or a mail's row, a cache key, a stored file. An app
    ///         reads its tenant as its own row, <c>Current.Tenant</c>.
    ///     </para>
    /// </remarks>
    /// <exception cref="Rask.Cqrs.ForbiddenException">
    ///     A resolver is registered and the signed-in user's claim names a different tenant than it did.
    /// </exception>
    internal static Guid? Key => KeyIn(Db.ScopeServices);

    /// <summary>The tenant the work in flight belongs to, or a thrown exception when there is none.</summary>
    /// <exception cref="InvalidOperationException">No tenant is set, the principal carries none, or <see cref="Across" /> is open.</exception>
    internal static Guid RequiredKey =>
        Key ?? throw new InvalidOperationException(
            "No tenant is set, so a tenant-scoped table cannot be read or written. Open one with " +
            "Tenant.Use(id) — a background job uses the tenant recorded on its own row — or say " +
            "Tenant.Across() when the work deliberately spans tenants.");

    /// <summary><see cref="Key" />, with <paramref name="scope" /> standing for the scope <see cref="Db.UseScope" /> opened.</summary>
    /// <remarks>What a live session asks before its event's scope is ambient, so the answer costs no scope of its own.</remarks>
    internal static Guid? KeyIn(IServiceProvider? scope) =>
        Ambient.Value is { AllTenants: false, Cleared: false } state ? state.Tenant ?? FromScope(scope) : null;

    /// <summary>
    ///     Makes <paramref name="tenant" /> the tenant until the returned scope is disposed.
    /// </summary>
    /// <param name="tenant">The tenant to work in.</param>
    /// <returns>A scope that restores the previous tenant.</returns>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tenant" /> is <see cref="Guid.AllBitsSet" />, which is reserved.
    /// </exception>
    public static IDisposable Use(Guid tenant) =>
        tenant == Nobody
            ? throw new ArgumentException(
                "Guid.AllBitsSet is not a tenant: it is what a read compares against when no tenant is " +
                "resolved, so that it matches nothing. Open the tenant by its own id — Tenant.Use(id).",
                nameof(tenant))
            : new Scope(new State(tenant, AllTenants: false, Cleared: false));

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
    ///         Inside the framework the tenant is still a <see cref="Guid" /> — a job's row, a cache key — while
    ///         <c>Current.Tenant.Id</c> is the number again; the number travels inside it by a fixed rule: the first eight bytes are zero and
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
    public static IDisposable Across() => new Scope(new State(Tenant: null, AllTenants: true, Cleared: false));

    /// <summary>Clears the tenant for the duration of the returned scope.</summary>
    /// <returns>A scope that restores the previous tenant.</returns>
    /// <remarks>
    ///     For a test, or for the sign-in that has to find a user before it can know their tenant. It clears the
    ///     signed-in user's tenant and the resolver's as well as an explicit one: inside it there is no tenant, so
    ///     <c>Current.Tenant</c> is null and a tenant-scoped read throws.
    /// </remarks>
    public static IDisposable None() => new Scope(new State(Tenant: null, AllTenants: false, Cleared: true));

    /// <summary>
    ///     The tenant the scope <see cref="Db.UseScope" /> opened says the work is for: the application's
    ///     resolver when it registered one, and the signed-in user's claim when it did not.
    /// </summary>
    /// <exception cref="Rask.Cqrs.ForbiddenException">
    ///     A resolver is registered and the signed-in user's claim names a different tenant than it did.
    /// </exception>
    internal static Guid? FromScope(IServiceProvider? scope)
    {
        var claimed = CurrentUser.ClaimedGuid(scope, ClaimType);

        if (ResolvedTenant.For(scope) is not { } resolved)
        {
            return claimed;
        }

        // The resolver decides. A user with no tenant claim — an administrator, or anybody in an app that
        // signs people in itself — works in the tenant it named. One whose claim names ANOTHER tenant is
        // somebody signed in to one customer and asking for a second, and is refused rather than obeyed.
        return resolved.Tenant is { } named && claimed is { } theirs && theirs != named
            ? throw new Rask.Cqrs.ForbiddenException(
                "The signed-in user belongs to a different tenant than the one this request is for.",
                isAuthenticated: true)
            : resolved.Tenant;
    }

    /// <summary>
    ///     Whether the application's resolver was asked and named no tenant, and nothing else names one.
    /// </summary>
    /// <remarks>
    ///     The one state in which a tenant-scoped table reads as empty and refuses a write. False in an app
    ///     with no resolver, where the same lack of a tenant throws as it always has.
    /// </remarks>
    internal static bool IsUnresolved =>
        Ambient.Value is { AllTenants: false, Tenant: null, Cleared: false } && ResolvedTenant.For(Db.ScopeServices) is { Tenant: null };

    /// <summary>Refuses a write to <paramref name="entity" /> when it is tenant-scoped and no tenant is resolved.</summary>
    /// <exception cref="MissingTenantException">The resolver named no tenant for this work.</exception>
    internal static void DemandForWrite(Type entity)
    {
        if (ConventionRegistry.IsPerTenant(entity) && IsUnresolved)
        {
            throw MissingTenantException.ForWrite(entity.Name);
        }
    }

    /// <summary>
    ///     What a context's tenant filter compares against: an explicit scope first, then the resolver or the
    ///     principal.
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
    ///     <para>
    ///         When a resolver registered with <c>AddRaskTenant</c> named no tenant, this is
    ///         <see cref="Guid.AllBitsSet" /> — a value no row holds — and NOT null: the read matches nothing
    ///         rather than everything.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     Nothing says which tenant: no scope is open, the principal carries no tenant and no resolver is
    ///     registered.
    /// </exception>
    public static Guid? Resolve()
    {
        if (Ambient.Value.AllTenants)
        {
            return null;
        }

        return Key ?? (IsUnresolved ? Nobody : RequiredKey);
    }

    // Cleared is None's: default(State) is "nothing explicit", which still lets the principal say which tenant.
    private readonly record struct State(Guid? Tenant, bool AllTenants, bool Cleared);

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
