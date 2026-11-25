using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;

namespace Rask.Data;

/// <summary>
///     What the generated <c>Current.Tenant</c> and <c>Current.User</c> read through: the app's own tenant and user
///     rows, held per key.
/// </summary>
/// <remarks>
///     <para>
///         Not a type to call. The build finds the aggregate that says <see cref="ITenant" /> — and the one that says
///         <see cref="IUser" /> — and emits the typed accessors and the registration that names them here; this
///         is public only because that generated code lives in the app's assembly.
///     </para>
///     <para>
///         The rows are read as work starts — a request, a live event, a background job, a mail, an outbox
///         message — so the accessors are synchronous lookups.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CurrentRows
{
    /// <summary>The meter the blocking reads are counted on.</summary>
    internal const string MeterName = "Rask.Data";

    /// <summary>The counter of reads answered by a synchronous query.</summary>
    internal const string QueriesName = "rask.data.current_row.queries";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Queries = Meter.CreateCounter<long>(
        QueriesName,
        "{query}",
        "Current.Tenant or Current.User read with nothing held for its key, and answered by a synchronous query. "
        + "Work the framework starts reads the row ahead, so a steady count means work it did not start.");

    // The rows the app registered, for the whole process — and a test's own, for its flow alone (see Use).
    private static readonly AsyncLocal<Registrations?> InFlow = new();
    private static CurrentRow? _registeredTenant;
    private static CurrentRow? _registeredUser;

    /// <summary>Names the app's tenant aggregate and how its read face is found by key. Generated code calls this.</summary>
    /// <typeparam name="TAggregate">The aggregate that says <see cref="ITenant" />.</typeparam>
    /// <typeparam name="TRead">Its read face.</typeparam>
    /// <typeparam name="TKey">Its key.</typeparam>
    /// <param name="byKey">The read face's row keyed by a value: <c>key =&gt; row =&gt; row.Id == key</c>.</param>
    /// <param name="keyOf">A read face's key: <c>row =&gt; row.Id</c>.</param>
    public static void UseTenant<TAggregate, [DynamicallyAccessedMembers(DataTrimming.Entity)] TRead, TKey>(
        Func<TKey, Expression<Func<TRead, bool>>> byKey, Func<TRead, TKey> keyOf)
        where TAggregate : IAggregate, ITenant
        where TRead : class, IReadModel
    {
        ArgumentNullException.ThrowIfNull(byKey);
        ArgumentNullException.ThrowIfNull(keyOf);
        _registeredTenant = new CurrentRow<TRead, TKey>(CurrentRowKind.Tenant, typeof(TAggregate), byKey, keyOf);
    }

    /// <summary>Names the app's user aggregate and how its read face is found by key. Generated code calls this.</summary>
    /// <typeparam name="TAggregate">The aggregate that says <see cref="IUser" />.</typeparam>
    /// <typeparam name="TRead">Its read face.</typeparam>
    /// <typeparam name="TKey">Its key.</typeparam>
    /// <param name="byKey">The read face's row keyed by a value: <c>key =&gt; row =&gt; row.Id == key</c>.</param>
    /// <param name="keyOf">A read face's key: <c>row =&gt; row.Id</c>.</param>
    public static void UseUser<TAggregate, [DynamicallyAccessedMembers(DataTrimming.Entity)] TRead, TKey>(
        Func<TKey, Expression<Func<TRead, bool>>> byKey, Func<TRead, TKey> keyOf)
        where TAggregate : IAggregate, IUser
        where TRead : class, IReadModel
    {
        ArgumentNullException.ThrowIfNull(byKey);
        ArgumentNullException.ThrowIfNull(keyOf);
        _registeredUser = new CurrentRow<TRead, TKey>(CurrentRowKind.User, typeof(TAggregate), byKey, keyOf);
    }

    /// <summary>The tenant's row, or <see langword="null" /> when the work belongs to no tenant. <c>Current.Tenant</c>.</summary>
    /// <typeparam name="TRead">The tenant's read face.</typeparam>
    /// <returns>The row, or <see langword="null" />.</returns>
    /// <exception cref="MissingTenantException">The tenant in flight has no row.</exception>
    public static TRead? Tenant<TRead>()
        where TRead : class =>
        Data.Tenant.Key is { } key ? (TRead)Registered(TenantRow, CurrentRowKind.Tenant).Read(key) : null;

    /// <summary>The tenant's row, or a thrown exception when the work belongs to no tenant. <c>Current.RequiredTenant</c>.</summary>
    /// <typeparam name="TRead">The tenant's read face.</typeparam>
    /// <returns>The row.</returns>
    /// <exception cref="InvalidOperationException">No tenant is set, or <c>Tenant.Across()</c> is open.</exception>
    /// <exception cref="MissingTenantException">The tenant in flight has no row.</exception>
    public static TRead RequiredTenant<TRead>()
        where TRead : class =>
        (TRead)Registered(TenantRow, CurrentRowKind.Tenant).Read(Data.Tenant.RequiredKey);

    /// <summary>The user's row, or <see langword="null" /> when the work is for nobody. <c>Current.User</c>.</summary>
    /// <typeparam name="TRead">The user's read face.</typeparam>
    /// <returns>The row, or <see langword="null" />.</returns>
    /// <exception cref="InvalidOperationException">The user in flight has no row.</exception>
    public static TRead? User<TRead>()
        where TRead : class =>
        CurrentUser.Id is { } key ? (TRead)Registered(UserRow, CurrentRowKind.User).Read(key) : null;

    /// <summary>The user's row, or a thrown exception when the work is for nobody. <c>Current.RequiredUser</c>.</summary>
    /// <typeparam name="TRead">The user's read face.</typeparam>
    /// <returns>The row.</returns>
    /// <exception cref="InvalidOperationException">Nobody is signed in, or the user in flight has no row.</exception>
    public static TRead RequiredUser<TRead>()
        where TRead : class =>
        (TRead)Registered(UserRow, CurrentRowKind.User).Read(CurrentUser.RequiredId);

    /// <summary>
    ///     The id of the user the work in flight is for, as the number the app's user is keyed by, or
    ///     <see langword="null" /> when it is for nobody. <c>Current.UserId</c>, for an app whose user is keyed by an
    ///     <see cref="int" /> or a <see cref="long" />.
    /// </summary>
    /// <typeparam name="TKey">The user aggregate's key.</typeparam>
    /// <returns>The id, or <see langword="null" />.</returns>
    /// <exception cref="InvalidOperationException">The user in flight is an identifier that carries no such number.</exception>
    public static TKey? UserId<TKey>()
        where TKey : struct =>
        CurrentUser.Id is { } id ? CurrentKey.Of<TKey>(id, CurrentRowKind.User, KeyedBy(UserRow, "The user")) : null;

    /// <summary>
    ///     The id of the user the work in flight is for, as the number the app's user is keyed by, or a thrown
    ///     exception when it is for nobody. <c>Current.RequiredUserId</c>, for a numbered user.
    /// </summary>
    /// <typeparam name="TKey">The user aggregate's key.</typeparam>
    /// <returns>The id.</returns>
    /// <exception cref="InvalidOperationException">
    ///     No user is signed in, or the user in flight is an identifier that carries no such number.
    /// </exception>
    public static TKey RequiredUserId<TKey>()
        where TKey : struct =>
        CurrentKey.Of<TKey>(CurrentUser.RequiredId, CurrentRowKind.User, KeyedBy(UserRow, "The user"));

    /// <summary>
    ///     Makes the user keyed <paramref name="userId" /> the current user until the returned scope is disposed.
    ///     <c>Current.UseUser</c>, for a numbered user.
    /// </summary>
    /// <typeparam name="TKey">The user aggregate's key.</typeparam>
    /// <param name="userId">The user to work for, or <see langword="null" /> to work for nobody.</param>
    /// <returns>A scope that restores the previous user.</returns>
    public static IDisposable WorkFor<TKey>(TKey? userId)
        where TKey : struct =>
        Current.UseUser(userId is { } key ? CurrentKey.ToGuid(key) : null);

    /// <summary>
    ///     The id of the tenant the work in flight belongs to, as the app's tenant is keyed, or
    ///     <see langword="null" /> when it belongs to none — or to all, inside <c>Tenant.Across()</c>.
    ///     <c>Current.TenantId</c>.
    /// </summary>
    /// <typeparam name="TKey">The tenant aggregate's key: an <see cref="int" />, a <see cref="long" /> or a <see cref="Guid" />.</typeparam>
    /// <returns>The id, or <see langword="null" />.</returns>
    /// <exception cref="InvalidOperationException">The tenant in flight is an identifier that carries no such number.</exception>
    public static TKey? TenantId<TKey>()
        where TKey : struct =>
        Data.Tenant.Key is { } key ? CurrentKey.Of<TKey>(key, CurrentRowKind.Tenant, KeyedBy(TenantRow, "The tenant")) : null;

    /// <summary>
    ///     The id of the tenant the work in flight belongs to, as the app's tenant is keyed, or a thrown exception
    ///     when it belongs to none. <c>Current.RequiredTenantId</c>.
    /// </summary>
    /// <typeparam name="TKey">The tenant aggregate's key: an <see cref="int" />, a <see cref="long" /> or a <see cref="Guid" />.</typeparam>
    /// <returns>The id.</returns>
    /// <exception cref="InvalidOperationException">
    ///     No tenant is set, <c>Tenant.Across()</c> is open, or the tenant in flight carries no such number.
    /// </exception>
    public static TKey RequiredTenantId<TKey>()
        where TKey : struct
    {
        if (Data.Tenant.IsAcrossTenants)
        {
            throw new InvalidOperationException(
                "Tenant.Across() is open, so the work in flight spans every tenant and belongs to none of them: there " +
                "is no Current.RequiredTenantId. Read Current.TenantId and handle null, or open the one tenant the " +
                "work is for inside it — Tenant.Use(id).");
        }

        return CurrentKey.Of<TKey>(Data.Tenant.RequiredKey, CurrentRowKind.Tenant, KeyedBy(TenantRow, "The tenant"));
    }

    /// <summary>Whether the app's user aggregate is keyed by a number, so a numeric user claim names a user.</summary>
    internal static bool NumberedUser => UserRow?.IsNumbered == true;

    /// <summary>Whether the app's tenant aggregate is keyed by a number, so a numeric tenant claim names a tenant.</summary>
    internal static bool NumberedTenant => TenantRow?.IsNumbered == true;

    /// <summary>Whether the app named a tenant or a user aggregate, so there is anything to read ahead or drop.</summary>
    internal static bool IsOn => TenantRow is not null || UserRow is not null;

    /// <summary>Reads the rows of the tenant and the user the work in flight is for, unless they are held already.</summary>
    /// <remarks>Completes synchronously, allocating nothing, when there is nothing to read.</remarks>
    internal static ValueTask Prime(CancellationToken cancellationToken) => Prime(null, cancellationToken);

    /// <summary>
    ///     <see cref="Prime(CancellationToken)" /> for the work of <paramref name="scope" /> — a live session's,
    ///     before its event's scope is ambient — or of the ambient scope when null.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Completes synchronously, allocating nothing, when both rows are held: this runs before every event.
    ///     </para>
    ///     <para>
    ///         It never fails the work it runs ahead of. A key with no row, a database that is down, a resolver
    ///         that refuses: whatever reads <c>Current.Tenant</c> asks again, and is the one that is told — work that
    ///         never reads it is not failed for it.
    ///     </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Reading ahead is work for a read that may never come: the read itself asks again and "
                        + "throws whatever this would have, at the line that wanted the row.")]
    internal static ValueTask Prime(IServiceProvider? scope, CancellationToken cancellationToken)
    {
        var tenant = TenantRow;
        var user = UserRow;
        if (tenant is null && user is null)
        {
            return default;
        }

        try
        {
            scope ??= Db.ScopeServices;
            var claims = PrincipalClaims.In(scope);
            var tenantKey = tenant is not null ? Data.Tenant.KeyIn(scope, claims) : null;
            var userKey = user is not null ? CurrentUser.UserIdIn(scope, claims) : null;
            var tenantOwed = tenantKey is { } t && !tenant!.Holds(scope, t) ? tenant : null;
            var userOwed = userKey is { } u && !user!.Holds(scope, u) ? user : null;

            if (tenantOwed is null && userOwed is null)
            {
                return default;
            }

            return PrimeLater(scope, tenantOwed, tenantKey, userOwed, userKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return default;
        }
    }

    /// <summary>Drops the rows held for any of <paramref name="saved" />: a save of the tenant or the user changed them.</summary>
    internal static void Saved(IEnumerable<Type> saved)
    {
        var tenant = TenantRow;
        var user = UserRow;
        if (tenant is null && user is null)
        {
            return;
        }

        foreach (var type in saved)
        {
            if (tenant?.Aggregate == type)
            {
                tenant.Forget();
            }

            if (user?.Aggregate == type)
            {
                user.Forget();
            }
        }
    }

    /// <summary>Whether a save of <paramref name="type" /> drops a held row: it is the tenant or the user aggregate.</summary>
    internal static bool Watches(Type type) => TenantRow?.Aggregate == type || UserRow?.Aggregate == type;

    /// <summary>Counts a read answered by a synchronous query.</summary>
    internal static void CountQuery(CurrentRowKind kind) =>
        Queries.Add(1, new KeyValuePair<string, object?>("row", kind is CurrentRowKind.Tenant ? "tenant" : "user"));

    /// <summary>
    ///     Registers <paramref name="row" /> as the generated code does, for the calling flow alone — the test that calls it
    ///     and what it awaits — so tests side by side each read their own rows. Test seam: a test assembly cannot say
    ///     ITenant four times.
    /// </summary>
    /// <remarks>
    ///     The flow then sees only what it registered: a kind it did not register is none, not the process's. Not async,
    ///     so it registers in the flow of whatever calls it.
    /// </remarks>
    internal static void Use(CurrentRow row)
    {
        var current = InFlow.Value ?? Registrations.None;
        InFlow.Value = row.Kind is CurrentRowKind.Tenant ? current with { ForTenant = row } : current with { ForUser = row };
    }

    /// <summary>Forgets what <see cref="Use" /> registered in this flow. Test seam, like <see cref="Db.Reset" />.</summary>
    internal static void Reset() => InFlow.Value = null;

    private static CurrentRow? TenantRow => InFlow.Value is { } flow ? flow.ForTenant : _registeredTenant;

    private static CurrentRow? UserRow => InFlow.Value is { } flow ? flow.ForUser : _registeredUser;

    private sealed record Registrations(CurrentRow? ForTenant, CurrentRow? ForUser)
    {
        internal static readonly Registrations None = new(null, null);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "As Prime: the read that wants the row asks again and is told.")]
    private static async ValueTask PrimeLater(
        IServiceProvider? scope,
        CurrentRow? tenant,
        Guid? tenantKey,
        CurrentRow? user,
        Guid? userKey,
        CancellationToken cancellationToken)
    {
        // Read in the work's own scope, so the context is built for its user and its tenant filter holds.
        using var ambient = scope is not null && !ReferenceEquals(scope, Db.ScopeServices) ? Db.UseScope(scope) : null;

        try
        {
            if (tenant is not null && tenantKey is { } t)
            {
                await tenant.Prime(t, cancellationToken).ConfigureAwait(false);
            }

            if (user is not null && userKey is { } u)
            {
                await user.Prime(u, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // See the justification above.
        }
    }

    private static string KeyedBy(CurrentRow? row, string fallback) => row?.Aggregate.Name ?? fallback;

    private static CurrentRow Registered(CurrentRow? row, CurrentRowKind kind) =>
        row ?? throw new InvalidOperationException(
            kind is CurrentRowKind.Tenant
                ? "No aggregate says ITenant, so there is no tenant row to read. Say it on the app's tenant: " +
                  "public sealed class Tenant : Aggregate<int>, ITenant."
                : "No aggregate says IUser, so there is no user row to read. Say it on the app's user — " +
                  "public sealed class User : Aggregate<Guid>, IUser — or derive it from Rask.Auth's Authenticatable.");
}
