using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>An <see cref="ITypedTenantColumn" /> of the typed id <typeparamref name="TKey" />.</summary>
/// <typeparam name="TKey">The tenant's typed id.</typeparam>
/// <typeparam name="TValue">What it holds: a <see cref="Guid" />, an <see cref="int" /> or a <see cref="long" />.</typeparam>
/// <param name="wrap">The id holding a value: <c>value =&gt; new TenantId(value)</c>.</param>
/// <param name="unwrap">The value an id holds: <c>id =&gt; id.Value</c>.</param>
internal sealed class TypedTenantColumn<TKey, TValue>(Func<TValue, TKey> wrap, Func<TKey, TValue> unwrap) : ITypedTenantColumn
    where TKey : struct
    where TValue : struct
{
    // Captured from real expressions, as TenantColumn's own readers are, so the trimmer follows both.
    private static readonly MethodInfo StoredProperty =
        Call((Expression<Func<object, TKey?>>)(e => EF.Property<TKey?>(e, Columns.TenantId)));

    private static readonly MethodInfo Reader =
        Call((Expression<Func<TypedTenantColumn<TKey, TValue>, ITenantScoped, string, TKey?>>)(
            (column, scope, table) => column.CurrentOf(scope, table)));

    /// <inheritdoc />
    public Type Key => typeof(TKey);

    /// <inheritdoc />
    public Type Column => typeof(TKey?);

    /// <inheritdoc />
    public Type Value => typeof(TValue?);

    /// <inheritdoc />
    public MethodInfo Stored => StoredProperty;

    /// <inheritdoc />
    public Expression Current(Expression scope, Expression table) =>
        Expression.Call(Expression.Constant(this), Reader, scope, table);

    /// <inheritdoc />
    public object ValueFor(Guid tenant, string table) => wrap(TenantColumn.ValueAs<TValue>(tenant, table));

    /// <inheritdoc />
    public Guid? Carried(object stored) => stored is TKey key ? CurrentKey.ToGuid(unwrap(key)) : null;

    /// <summary>
    ///     The tenant in flight as the typed id, or null for nobody and for every tenant, as
    ///     <see cref="TenantColumn.CurrentGuid" /> answers for a <c>Guid?</c> column.
    /// </summary>
    internal TKey? CurrentOf(ITenantScoped scope, string table) =>
        TenantColumn.Named(scope) is { } tenant ? wrap(TenantColumn.ValueAs<TValue>(tenant, table)) : null;

    private static MethodInfo Call(LambdaExpression lambda) => ((MethodCallExpression)lambda.Body).Method;
}
