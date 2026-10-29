using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The <c>TenantId</c> column of a <see cref="IPerTenant" /> table, in the type the entity declared
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
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    // EF.Property<T>(entity, "TenantId") per supported type, and the readers the filter calls — each captured
    // from a real expression rather than through MakeGenericMethod, so the trimmer can follow all of them.
    private static readonly MethodInfo StoredGuid =
        Call((Expression<Func<object, Guid?>>)(e => EF.Property<Guid?>(e, Columns.TenantId)));

    private static readonly MethodInfo StoredInt32 =
        Call((Expression<Func<object, int?>>)(e => EF.Property<int?>(e, Columns.TenantId)));

    private static readonly MethodInfo StoredInt64 =
        Call((Expression<Func<object, long?>>)(e => EF.Property<long?>(e, Columns.TenantId)));

    private static readonly MethodInfo InlinedBool =
        Call((Expression<Func<bool, bool>>)(flag => EF.Constant(flag)));

    private static readonly MethodInfo InlinedGuid =
        Call((Expression<Func<Guid?, Guid?>>)(tenant => EF.Constant(tenant)));

    private static readonly MethodInfo InlinedInt32 =
        Call((Expression<Func<int?, int?>>)(tenant => EF.Constant(tenant)));

    private static readonly MethodInfo InlinedInt64 =
        Call((Expression<Func<long?, long?>>)(tenant => EF.Constant(tenant)));

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
    ///         The <c>unrestricted</c> arm is read through <see cref="EF.Constant{T}" />, so EF Core folds it into
    ///         the query when it is built (and caches one plan for <c>Across</c>, one without) instead of binding
    ///         a parameter: <c>@flag = 1 OR TenantId = @t</c> is a catch-all SQL Server cannot seek an index with.
    ///     </para>
    ///     <para>
    ///         On SQL Server the tenant is folded in the same way (<see cref="InlinesTenant" />), so each tenant
    ///         compiles a plan of its own: one plan bound to <c>@t</c> is sniffed for the first tenant that ran it,
    ///         and a nested loop sized for 734 rows then reads another tenant's 87 000 (measured: 1.45 s against
    ///         0.41 s). The value is the tenant in flight, rendered by EF Core as a typed literal each time the
    ///         command is built — one compiled query for every tenant, its SQL never cached — so no tenant's SQL
    ///         is ever reused for another. Other providers keep the parameter: PostgreSQL plans an unprepared
    ///         statement with the value it is given, and SQLite has no plan cache shared across connections.
    ///     </para>
    /// </remarks>
    internal static LambdaExpression BuildFilter(Type clrType, Type column, DbContext context)
    {
        var entity = Expression.Parameter(clrType, "e");
        var scope = Expression.Convert(Expression.Constant(context), typeof(ITenantScoped));
        var (storedReader, currentReader) = ReadersFor(column);

        var stored = Expression.Call(storedReader, entity, Expression.Constant(Columns.TenantId));
        Expression current = Expression.Call(currentReader, scope, Expression.Constant(clrType.Name));

        if (InlinesTenant(context))
        {
            current = Expression.Call(InlinedReaderFor(column), current);
        }

        var owned = Expression.AndAlso(
            Expression.NotEqual(stored, Expression.Constant(null, column)),
            Expression.Equal(stored, current));

        return Expression.Lambda(Expression.OrElse(UnrestrictedArm(context), owned), entity);
    }

    /// <summary>
    ///     The <c>unrestricted</c> arm every tenant filter opens with, folded into the query when it is built so
    ///     <see cref="Tenant.Across" /> and a tenant in flight each get a plan of their own.
    /// </summary>
    internal static Expression UnrestrictedArm(DbContext context) =>
        Expression.Call(
            InlinedBool,
            Expression.Call(UnrestrictedReader, Expression.Convert(Expression.Constant(context), typeof(ITenantScoped))));

    /// <summary>
    ///     Whether the tenant in flight is folded into the SQL of <paramref name="context" />'s reads: on SQL Server
    ///     only, whose cached plans are otherwise shared by every tenant.
    /// </summary>
    internal static bool InlinesTenant(DbContext context) =>
        string.Equals(context.Database.ProviderName, SqlServerProvider, StringComparison.Ordinal);

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

        return column == typeof(long?) ? Int64(tenant, table) : tenant;
    }

    internal static bool Unrestricted(ITenantScoped scope) => scope.CurrentTenant is null;

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
    private static Guid? Named(ITenantScoped scope) =>
        scope.CurrentTenant is { } tenant && tenant != Tenant.Nobody ? tenant : null;

    private static MethodInfo InlinedReaderFor(Type column)
    {
        if (column == typeof(int?))
        {
            return InlinedInt32;
        }

        return column == typeof(long?) ? InlinedInt64 : InlinedGuid;
    }

    private static (MethodInfo Stored, MethodInfo Current) ReadersFor(Type column)
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
                $"'{entity.Name}' implements IPerTenant<{key.Name}> but declares a TenantId of type '{Display(declared)}'. " +
                $"Make them agree: declare `public {key.Name}? TenantId {{ get; private set; }}`, or name " +
                $"IPerTenant<{Display(declared).TrimEnd('?')}>.");
        }

        return column == typeof(Guid?) || column == typeof(int?) || column == typeof(long?)
            ? column
            : throw new InvalidOperationException(
                $"'{entity.Name}' implements IPerTenant and declares a TenantId of type '{Display(column)}'. " +
                "A tenant-scoped table keeps its tenant in a Guid?, an int? or a long? — nullable, because a row " +
                "has none until it is saved. Declare `public int? TenantId { get; private set; }`, or leave " +
                "the property out and name " +
                "IPerTenant<int> (or plain IPerTenant for a Guid?) on the aggregate.");
    }

    // IPerTenant<int> names the column a table with no TenantId property gets; plain IPerTenant keeps a Guid?.
    private static Type ShadowColumnFor(Type entity) =>
        ConventionRegistry.TenantKeyFor(entity) switch
        {
            { } key when key == typeof(int) => typeof(int?),
            { } key when key == typeof(long) => typeof(long?),
            _ => typeof(Guid?),
        };

    private static string Display(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

    private static MethodInfo Call(LambdaExpression lambda) => ((MethodCallExpression)lambda.Body).Method;
}
