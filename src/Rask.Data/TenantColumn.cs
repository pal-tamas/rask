using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The <c>TenantId</c> column of a <see cref="Tenancy.PerTenant" /> table, in the type the entity declared
///     it: its query filter and the value an insert is stamped with.
/// </summary>
/// <remarks>
///     <para>
///         The declared property's type decides — <c>Guid?</c>, <c>int?</c> or <c>long?</c> — and an entity
///         that declares nothing keeps a shadow <c>Guid?</c>. The framework goes on carrying the tenant as a
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
        Call((Expression<Func<ITenantScoped, bool>>)(scope => Unrestricted(scope)));

    private static readonly MethodInfo GuidReader =
        Call((Expression<Func<ITenantScoped, string, Guid?>>)((scope, table) => CurrentGuid(scope, table)));

    private static readonly MethodInfo Int32Reader =
        Call((Expression<Func<ITenantScoped, string, int?>>)((scope, table) => CurrentInt32(scope, table)));

    private static readonly MethodInfo Int64Reader =
        Call((Expression<Func<ITenantScoped, string, long?>>)((scope, table) => CurrentInt64(scope, table)));

    /// <summary>The type <paramref name="entityType" /> keeps its tenant in: what it declared, or <c>Guid?</c>.</summary>
    /// <exception cref="InvalidOperationException">It declared a <c>TenantId</c> of a type no tenant is kept in.</exception>
    internal static Type TypeFor(IReadOnlyEntityType entityType) =>
        Supported(entityType.FindProperty(Columns.TenantId)?.ClrType, entityType.ClrType);

    /// <summary>The same answer read off the class, for a read face built with no write model to mirror.</summary>
    internal static Type TypeFor([DynamicallyAccessedMembers(DataTrimming.Entity)] Type clrType) =>
        Supported(clrType.GetProperty(Columns.TenantId)?.PropertyType, clrType);

    /// <summary>
    ///     <c>e =&gt; unrestricted || (e.TenantId != null &amp;&amp; e.TenantId == current)</c>, with the tenant read
    ///     through <paramref name="context" /> so EF Core binds it per query instead of inlining the first one.
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
    /// </remarks>
    internal static LambdaExpression BuildFilter(Type clrType, Type column, DbContext context)
    {
        var entity = Expression.Parameter(clrType, "e");
        var scope = Expression.Convert(Expression.Constant(context), typeof(ITenantScoped));
        var (storedReader, currentReader) = ReadersFor(column);

        var stored = Expression.Call(storedReader, entity, Expression.Constant(Columns.TenantId));
        var current = Expression.Call(currentReader, scope, Expression.Constant(clrType.Name));

        var owned = Expression.AndAlso(
            Expression.NotEqual(stored, Expression.Constant(null, column)),
            Expression.Equal(stored, current));

        return Expression.Lambda(Expression.OrElse(Expression.Call(UnrestrictedReader, scope), owned), entity);
    }

    /// <summary>What an insert into <paramref name="table" /> is stamped with for <paramref name="tenant" />.</summary>
    /// <exception cref="InvalidOperationException">The column keeps a number and the tenant is not one.</exception>
    internal static object ValueFor(Guid tenant, Type column, string table)
    {
        if (column == typeof(int?))
        {
            return Int32(tenant, table);
        }

        return column == typeof(long?) ? Int64(tenant, table) : tenant;
    }

    internal static bool Unrestricted(ITenantScoped scope) => scope.CurrentTenant is null;

    // The table is unused here and taken all the same: three readers of one shape are one call in the filter.
    internal static Guid? CurrentGuid(ITenantScoped scope, string table)
    {
        _ = table;
        return scope.CurrentTenant;
    }

    internal static int? CurrentInt32(ITenantScoped scope, string table) =>
        scope.CurrentTenant is { } tenant ? Int32(tenant, table) : null;

    internal static long? CurrentInt64(ITenantScoped scope, string table) =>
        scope.CurrentTenant is { } tenant ? Int64(tenant, table) : null;

    private static (MethodInfo Stored, MethodInfo Current) ReadersFor(Type column)
    {
        if (column == typeof(int?))
        {
            return (StoredInt32, Int32Reader);
        }

        return column == typeof(long?) ? (StoredInt64, Int64Reader) : (StoredGuid, GuidReader);
    }

    private static int Int32(Guid tenant, string table) =>
        TenantNumber.TryRead(tenant, out var number) && number is >= int.MinValue and <= int.MaxValue
            ? (int)number
            : throw NotANumber(tenant, table, "an int");

    private static long Int64(Guid tenant, string table) =>
        TenantNumber.TryRead(tenant, out var number) ? number : throw NotANumber(tenant, table, "a long");

    private static InvalidOperationException NotANumber(Guid tenant, string table, string kind) =>
        new($"The tenant in flight is {tenant}, and '{table}' keeps its tenant as {kind}, which that is not. " +
            "Say which tenant by its number — Tenant.Use(42) — wherever this table is read or written.");

    private static Type Supported(Type? declared, Type entity)
    {
        var column = declared ?? typeof(Guid?);

        return column == typeof(Guid?) || column == typeof(int?) || column == typeof(long?)
            ? column
            : throw new InvalidOperationException(
                $"'{entity.Name}' declares Scope = Tenancy.PerTenant and a TenantId of type '{Display(column)}'. " +
                "A tenant-scoped table keeps its tenant in a Guid?, an int? or a long? — nullable, because a row " +
                "has none until it is saved. Declare `public int? TenantId { get; private set; }`, or leave the " +
                "property out and Rask keeps a Guid? column of its own.");
    }

    private static string Display(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

    private static MethodInfo Call(LambdaExpression lambda) => ((MethodCallExpression)lambda.Body).Method;
}
