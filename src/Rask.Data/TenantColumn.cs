using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The <c>TenantId</c> column of a <see cref="IBelongsTo{TTenant}" /> table, in the type the entity declared
///     it: its query filter and the value an insert is stamped with.
/// </summary>
/// <remarks>
///     <para>
///         The declared property's type decides — <c>Guid?</c>, <c>int?</c>, <c>long?</c>, or the tenant's own
///         typed id over one of them (<see cref="ITypedTenantColumn" />) — and an entity that declares nothing keeps
///         a shadow column of its tenant's key — <c>Guid?</c> for a <c>Guid</c> or <c>string</c> tenant. The framework goes on carrying the tenant as a
///         <see cref="Guid" /> everywhere else; an integer column compares against, and is stamped with, the
///         number that <see cref="Guid" /> carries (<see cref="TenantNumber" />).
///     </para>
///     <para>
///         A tenant that is not a number, met by a table that keeps one, is REFUSED rather than compared: there
///         is no integer it could honestly be, and a guess is how one customer reads another's rows.
///     </para>
/// </remarks>
internal static class TenantColumn
{
    // EF.Property<T>(entity, "TenantId") per supported type, and the readers the filter calls — each captured
    // from a real expression rather than through MakeGenericMethod, so the trimmer can follow all of them.
    private static readonly MethodInfo StoredGuid =
        Call((Expression<Func<object, Guid?>>)(e => EF.Property<Guid?>(e, Columns.TenantId)));

    private static readonly MethodInfo StoredInt32 =
        Call((Expression<Func<object, int?>>)(e => EF.Property<int?>(e, Columns.TenantId)));

    private static readonly MethodInfo StoredInt64 =
        Call((Expression<Func<object, long?>>)(e => EF.Property<long?>(e, Columns.TenantId)));

    private static readonly MethodInfo UnrestrictedReader =
        Call((Expression<Func<ITenantScoped, int?>>)(scope => AcrossMarker(scope)));

    // EF.Parameter around the marker: without it EF Core evaluates the whole `marker == null` up front and binds the
    // answer as ONE bool parameter, which stays in the SQL as `@p OR …`. As a parameter of its own, the NULL test is
    // left to EF Core's SQL processor, which folds it from the value.
    private static readonly MethodInfo AsParameter =
        Call((Expression<Func<int?, int?>>)(marker => EF.Parameter(marker)));

    private static readonly MethodInfo GuidReader =
        Call((Expression<Func<ITenantScoped, string, Guid?>>)((scope, table) => CurrentGuid(scope, table)));

    private static readonly MethodInfo Int32Reader =
        Call((Expression<Func<ITenantScoped, string, int?>>)((scope, table) => CurrentInt32(scope, table)));

    private static readonly MethodInfo Int64Reader =
        Call((Expression<Func<ITenantScoped, string, long?>>)((scope, table) => CurrentInt64(scope, table)));

    // The typed ids a tenant is kept in, by the id: registered with the tenant, for the whole process — a column type
    // is a fact of the model, not of the work in flight.
    private static readonly ConcurrentDictionary<Type, ITypedTenantColumn> Typed = new();

    /// <summary>Makes <paramref name="column" />'s typed id one a tenant-scoped table may keep its tenant in.</summary>
    internal static void Know(ITypedTenantColumn column) => Typed[column.Key] = column;

    /// <summary>The typed column <paramref name="type" /> — the id, or the id nullable — is, or null.</summary>
    internal static ITypedTenantColumn? TypedFor(Type type) =>
        Typed.GetValueOrDefault(Nullable.GetUnderlyingType(type) ?? type);

    /// <summary>
    ///     The type a battery table that follows <paramref name="column" /> keeps: the value a typed id holds, else the
    ///     column itself.
    /// </summary>
    internal static Type ValueColumnOf(Type column) => TypedFor(column)?.Value ?? column;

    /// <summary>A stored typed id as the <see cref="Guid" /> the framework carries, or null when it is no tenant's id.</summary>
    internal static Guid? TypedCarried(object stored) => TypedFor(stored.GetType())?.Carried(stored);

    /// <summary>The type <paramref name="entityType" /> keeps its tenant in: what it declared, or <c>Guid?</c>.</summary>
    /// <exception cref="InvalidOperationException">It declared a <c>TenantId</c> of a type no tenant is kept in.</exception>
    internal static Type TypeFor(IReadOnlyEntityType entityType) =>
        Supported(entityType.FindProperty(Columns.TenantId)?.ClrType, entityType.ClrType);

    /// <summary>The same answer read off the class, for a read face built with no write model to mirror.</summary>
    internal static Type TypeFor([DynamicallyAccessedMembers(DataTrimming.Entity)] Type clrType) =>
        Supported(clrType.GetProperty(Columns.TenantId)?.PropertyType, clrType);

    /// <summary>
    ///     <c>e =&gt; unrestricted || (e.TenantId != null &amp;&amp; e.TenantId == current)</c>, with the tenant read
    ///     through <paramref name="context" /> so EF Core reads it per query instead of baking the first one into the model.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The <c>unrestricted</c> arm is what makes <see cref="Tenant.Across" /> mean "every tenant" rather
    ///         than "the rows nobody owns".
    ///     </para>
    ///     <para>
    ///         The <c>!= null</c> arm is not redundant. EF Core compares a column with a null parameter as
    ///         <c>IS NULL</c>, so without it a read that names no tenant would return exactly the rows that
    ///         belong to none — and a table that already existed may well hold some.
    ///     </para>
    ///     <para>
    ///         The <c>unrestricted</c> arm is a NULL test of a parameter (<see cref="AcrossMarker" />), which EF Core
    ///         folds to TRUE or FALSE from the parameter's value before the SQL is written, and caches one SQL text for
    ///         each: under <see cref="Tenant.Across" /> the filter is gone, in a tenant it is the plain equality, and
    ///         neither carries <c>@flag = 1 OR TenantId = @t</c>, a catch-all SQL Server cannot seek an index with. It was
    ///         <c>EF.Constant</c> until EF Core 11, which refuses that in a query filter; the tenant itself, which SQL
    ///         Server also had inlined per tenant against plan sniffing, is a parameter now.
    ///     </para>
    /// </remarks>
    internal static LambdaExpression BuildFilter(Type clrType, Type column, DbContext context)
    {
        var entity = Expression.Parameter(clrType, "e");
        var scope = Expression.Convert(Expression.Constant(context), typeof(ITenantScoped));
        var (stored, current) = ReadersFor(column, entity, scope, Expression.Constant(clrType.Name));

        var owned = Expression.AndAlso(
            Expression.NotEqual(stored, Expression.Constant(null, column)),
            Expression.Equal(stored, current));

        return Expression.Lambda(Expression.OrElse(UnrestrictedArm(context), owned), entity);
    }

    /// <summary>
    ///     The <c>unrestricted</c> arm every tenant filter opens with: true under <see cref="Tenant.Across" />, and
    ///     folded away by EF Core either way (<see cref="AcrossMarker" />).
    /// </summary>
    internal static Expression UnrestrictedArm(DbContext context) =>
        Expression.Equal(
            Expression.Call(
                AsParameter,
                Expression.Call(UnrestrictedReader, Expression.Convert(Expression.Constant(context), typeof(ITenantScoped)))),
            Expression.Constant(null, typeof(int?)));

    /// <summary>What an insert into <paramref name="table" /> with no tenant of its own is stamped with: the tenant in flight.</summary>
    /// <exception cref="InvalidOperationException">No tenant is in flight, or <see cref="Tenant.Across" /> is open.</exception>
    internal static object InFlight(Type column, string table) =>
        Tenant.IsAcrossTenants
            ? throw new InvalidOperationException(
                $"'{table}' is tenant-scoped and is being inserted inside " +
                "Tenant.Across(), which says which tenant it belongs to for nobody. Set TenantId on the row, " +
                "or open Tenant.Use(id) around the insert.")
            : ValueFor(Tenant.RequiredKey, column, table);

    /// <summary>What an insert into <paramref name="table" /> is stamped with for <paramref name="tenant" />.</summary>
    /// <exception cref="InvalidOperationException">The column keeps a number and the tenant is not one.</exception>
    /// <exception cref="MissingTenantException"><paramref name="tenant" /> is the value that stands for no tenant.</exception>
    internal static object ValueFor(Guid tenant, Type column, string table)
    {
        // The value a read with no tenant compares against is not a tenant, so it is never written as one.
        if (tenant == Tenant.Nobody)
        {
            throw MissingTenantException.ForWrite(table);
        }

        if (column == typeof(int?))
        {
            return Int32(tenant, table);
        }

        if (column == typeof(long?))
        {
            return Int64(tenant, table);
        }

        return TypedFor(column) is { } typed ? typed.ValueFor(tenant, table) : tenant;
    }

    /// <summary><paramref name="tenant" /> as the <typeparamref name="TValue" /> a column keeps it in.</summary>
    /// <typeparam name="TValue">A <see cref="Guid" />, an <see cref="int" /> or a <see cref="long" />.</typeparam>
    /// <exception cref="InvalidOperationException">The column keeps a number and the tenant is not one.</exception>
    internal static TValue ValueAs<TValue>(Guid tenant, string table)
        where TValue : struct
    {
        if (typeof(TValue) == typeof(int))
        {
            return (TValue)(object)Int32(tenant, table);
        }

        return typeof(TValue) == typeof(long) ? (TValue)(object)Int64(tenant, table) : (TValue)(object)tenant;
    }

    internal static bool Unrestricted(ITenantScoped scope) => scope.CurrentTenant is null;

    /// <summary>
    ///     NULL under <see cref="Tenant.Across" />, 1 otherwise: the filter asks <c>@marker IS NULL</c>, which EF Core
    ///     answers from the parameter's value and folds away, where a bool parameter would stay in the SQL as an OR.
    /// </summary>
    internal static int? AcrossMarker(ITenantScoped scope) => Unrestricted(scope) ? null : 1;

    // The table is unused here and taken all the same: three readers of one shape are one call in the filter.
    internal static Guid? CurrentGuid(ITenantScoped scope, string table)
    {
        _ = table;
        return Named(scope);
    }

    internal static int? CurrentInt32(ITenantScoped scope, string table) =>
        Named(scope) is { } tenant ? Int32(tenant, table) : null;

    internal static long? CurrentInt64(ITenantScoped scope, string table) =>
        Named(scope) is { } tenant ? Int64(tenant, table) : null;

    // Null for "nobody" as well as for "unrestricted". The first is what makes a read with no resolved tenant
    // empty: the filter then asks for `TenantId IS NOT NULL AND TenantId IS NULL`, which no row satisfies.
    internal static Guid? Named(ITenantScoped scope) =>
        scope.CurrentTenant is { } tenant && tenant != Tenant.Nobody ? tenant : null;

    private static (Expression Stored, Expression Current) ReadersFor(
        Type column, ParameterExpression entity, Expression scope, Expression table)
    {
        var name = Expression.Constant(Columns.TenantId);
        if (TypedFor(column) is { } typed)
        {
            return (Expression.Call(typed.Stored, entity, name), typed.Current(scope, table));
        }

        var (stored, current) = PlainReadersFor(column);
        return (Expression.Call(stored, entity, name), Expression.Call(current, scope, table));
    }

    private static (MethodInfo Stored, MethodInfo Current) PlainReadersFor(Type column)
    {
        if (column == typeof(int?))
        {
            return (StoredInt32, Int32Reader);
        }

        return column == typeof(long?) ? (StoredInt64, Int64Reader) : (StoredGuid, GuidReader);
    }

    internal static int Int32(Guid tenant, string table) =>
        TenantNumber.TryRead(tenant, out var number) && number is >= int.MinValue and <= int.MaxValue
            ? (int)number
            : throw NotANumber(tenant, table, "an int");

    internal static long Int64(Guid tenant, string table) =>
        TenantNumber.TryRead(tenant, out var number) ? number : throw NotANumber(tenant, table, "a long");

    private static InvalidOperationException NotANumber(Guid tenant, string table, string kind) =>
        new($"The tenant in flight is {tenant}, and '{table}' keeps its tenant as {kind}, which that is not. " +
            "Say which tenant by its number — Tenant.Use(42), or a number returned to services.AddRaskTenant(sp => …) — " +
            "wherever this table is read or written.");

    private static Type Supported(Type? declared, Type entity)
    {
        var column = declared ?? ShadowColumnFor(entity);

        if (declared is not null && ConventionRegistry.TenantKeyFor(entity) is { } key &&
            Nullable.GetUnderlyingType(declared) != key)
        {
            throw new InvalidOperationException(
                $"'{entity.Name}' belongs to a tenant keyed by {key.Name} but declares a TenantId of type '{Display(declared)}'. " +
                $"Make them agree: declare `public {key.Name}? TenantId {{ get; private set; }}`, or name " +
                "IBelongsTo<…> of a tenant keyed by that type.");
        }

        return column == typeof(Guid?) || column == typeof(int?) || column == typeof(long?) || TypedFor(column)?.Column == column
            ? column
            : throw new InvalidOperationException(
                $"'{entity.Name}' belongs to a tenant and declares a TenantId of type '{Display(column)}'. " +
                "A tenant-scoped table keeps its tenant in a Guid?, an int?, a long? or the tenant's own typed id " +
                "made nullable — nullable, because a row has none until it is saved. Declare " +
                "`public int? TenantId { get; private set; }`, or leave " +
                "the property out and name " +
                "let IBelongsTo<Tenant> give it the tenant's key. A typed id is the tenant's own once " +
                "the aggregate keyed by it says ITenant.");
    }

    // The tenant's key names the column a table with no TenantId property gets; a Guid or string tenant keeps a Guid?.
    private static Type ShadowColumnFor(Type entity) =>
        ConventionRegistry.TenantKeyFor(entity) switch
        {
            { } key when key == typeof(int) => typeof(int?),
            { } key when key == typeof(long) => typeof(long?),
            { } key when TypedFor(key) is { } typed => typed.Column,
            _ => typeof(Guid?),
        };

    private static string Display(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

    private static MethodInfo Call(LambdaExpression lambda) => ((MethodCallExpression)lambda.Body).Method;
}
