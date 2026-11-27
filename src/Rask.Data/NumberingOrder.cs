using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Rask.Data;

/// <summary>
///     The pieces of a <see cref="NumberedBuilderExtensions.Numbered{TEntity, TNumber, TWithin, TOrder}" />
///     declaration as queries need them: the order's members one by one, each as it is mapped — so the database orders
///     by its column, through whatever converter maps it — and the first one as the value a row's place is compared by.
/// </summary>
internal static class NumberingOrder
{
    // Captured from real expressions, then opened to their definitions, so the trimmer can follow them.
    private static readonly MethodInfo OrderByMethod = Definition(q => q.OrderBy(x => x));
    private static readonly MethodInfo ThenByMethod = Definition(q => q.OrderBy(x => x).ThenBy(x => x));
    private static readonly MethodInfo OrderByDescendingMethod = Definition(q => q.OrderByDescending(x => x));
    private static readonly MethodInfo ThenByDescendingMethod = Definition(q => q.OrderByDescending(x => x).ThenByDescending(x => x));
    private static readonly MethodInfo StringCompare = ((Func<string?, string?, int>)string.Compare).Method;

    /// <summary>The members of <paramref name="by" />: an anonymous type's, or itself — each as a lambda of its own.</summary>
    /// <remarks>
    ///     Each is ordered by as it is mapped, never unwrapped to its <c>.Value</c>: a typed id kept as text is stored
    ///     through a converter that is not Rask's own, and EF Core cannot translate the <see cref="Guid" /> behind it.
    /// </remarks>
    internal static IReadOnlyList<LambdaExpression> Members(LambdaExpression by)
    {
        var parameter = by.Parameters[0];
        var body = by.Body is UnaryExpression { NodeType: ExpressionType.Convert } cast ? cast.Operand : by.Body;
        var members = body is NewExpression { Arguments.Count: > 0 } several ? several.Arguments : [body];
        return [.. members.Select(member => Expression.Lambda(member, parameter))];
    }

    /// <summary>
    ///     The first member of the order as a row's place is compared by: a one-value value object's value where that
    ///     value has an order (a moment, a number, text), else the member as it is mapped.
    /// </summary>
    internal static LambdaExpression Placed(LambdaExpression first)
    {
        var compared = Comparable(first.Body);
        return compared == first.Body ? first : Expression.Lambda(compared, first.Parameters);
    }

    /// <summary><paramref name="rows" /> in the declared order, the first member first.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "Queryable.OrderBy/ThenBy are closed over the entity and a mapped member's own type, which the model roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "As above.")]
    internal static IQueryable<TEntity> Sorted<TEntity>(IQueryable<TEntity> rows, ParameterExpression row, IReadOnlyList<LambdaExpression> order, bool descending)
    {
        var expression = rows.Expression;
        for (var i = 0; i < order.Count; i++)
        {
            var member = Rebind.To(order[i], row);
            var method = (i, descending) switch
            {
                (0, false) => OrderByMethod,
                (0, true) => OrderByDescendingMethod,
                (_, false) => ThenByMethod,
                _ => ThenByDescendingMethod,
            };
            expression = Expression.Call(
                method.MakeGenericMethod(typeof(TEntity), member.Type), expression, Expression.Quote(Expression.Lambda(member, row)));
        }

        return rows.Provider.CreateQuery<TEntity>(expression);
    }

    /// <summary>Whether a member of <paramref name="type" /> has an order a database and C# agree on.</summary>
    internal static bool IsComparable(Type type)
    {
        var plain = Nullable.GetUnderlyingType(type) ?? type;
        return plain == typeof(string) || HasOrder(plain);
    }

    /// <summary>
    ///     <paramref name="member" /> compared with <paramref name="since" />, as a parameter — at or after it, or before
    ///     it. Only for a member that <see cref="IsComparable" />.
    /// </summary>
    internal static Expression Compared(Expression member, object since, bool atOrAfter)
    {
        var type = Nullable.GetUnderlyingType(member.Type) ?? member.Type;
        if (type == typeof(string))
        {
            var compared = Expression.Call(StringCompare, member, QueryValue.Of(since, typeof(string)));
            return atOrAfter
                ? Expression.GreaterThanOrEqual(compared, Expression.Constant(0))
                : Expression.LessThan(compared, Expression.Constant(0));
        }

        var value = QueryValue.Of(since, member.Type);
        return atOrAfter ? Expression.GreaterThanOrEqual(member, value) : Expression.LessThan(member, value);
    }

    // A one-value value object is compared by its value, which C# and the database both order. One whose value has no
    // order — a typed id's Guid — stays whole: nothing compares it, and its column may be text the Guid does not reach.
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The member is one of a mapped entity's, and its type's properties are what the model maps — rooted by it.")]
    private static Expression Comparable(Expression member)
    {
        var type = Nullable.GetUnderlyingType(member.Type) ?? member.Type;
        if (IsPlain(type) || type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance) is not { } value ||
            !IsComparable(value.PropertyType))
        {
            return member;
        }

        return Expression.Property(member, value);
    }

    private static bool IsPlain(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
        type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) ||
        type == typeof(TimeSpan);

    private static bool HasOrder(Type type) => IsPlain(type) && type != typeof(Guid) && type != typeof(bool) && !type.IsEnum;

    private static MethodInfo Definition(Expression<Func<IQueryable<int>, IQueryable<int>>> call) =>
        ((MethodCallExpression)call.Body).Method.GetGenericMethodDefinition();

    /// <summary>Puts one parameter in the place of a lambda's own, so several lambdas read one row.</summary>
    internal sealed class Rebind(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        internal static Expression To(LambdaExpression lambda, Expression row) => new Rebind(lambda.Parameters[0], row).Visit(lambda.Body);

        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
