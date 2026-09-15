using System.Linq.Expressions;

namespace Rask.Data;

/// <summary>
/// Reads property names out of the lambdas model-metadata builders take, in the shapes EF Core itself accepts for
/// <c>HasIndex</c>: <c>x =&gt; x.A</c>, <c>x =&gt; (object)x.A</c> and <c>x =&gt; new { x.A, x.B }</c>.
/// </summary>
internal static class PropertyExpressions
{
    /// <summary>The single property <paramref name="expression"/> names.</summary>
    /// <exception cref="ArgumentException">It names none, several, or something other than a plain property.</exception>
    public static string Single<TEntity>(Expression<Func<TEntity, object?>> expression, string parameter)
    {
        var names = Many(expression, parameter);
        return names.Count == 1
            ? names[0]
            : throw new ArgumentException($"'{parameter}' must name exactly one property.", parameter);
    }

    /// <summary>Every property <paramref name="expression"/> names, in declaration order.</summary>
    /// <exception cref="ArgumentException">It names none, or something other than plain properties.</exception>
    public static IReadOnlyList<string> Many<TEntity>(Expression<Func<TEntity, object?>> expression, string parameter)
    {
        var body = Unwrap(expression.Body);

        if (body is NewExpression anonymous)
        {
            return anonymous.Arguments.Count == 0
                ? throw new ArgumentException($"'{parameter}' must name at least one property.", parameter)
                : [.. anonymous.Arguments.Select(argument => Name(argument, expression, parameter))];
        }

        return [Name(body, expression, parameter)];
    }

    private static string Name<TEntity>(
        Expression node,
        Expression<Func<TEntity, object?>> expression,
        string parameter)
        => Unwrap(node) is MemberExpression { Expression: ParameterExpression } member
            ? member.Member.Name
            : throw new ArgumentException(
                $"'{parameter}' must name properties of {typeof(TEntity).Name} directly, as in x => x.Property " +
                $"or x => new {{ x.A, x.B }}, but was '{expression}'.",
                parameter);

    // A value-type property is boxed by the Func<TEntity, object?> signature; see through that cast.
    private static Expression Unwrap(Expression node)
        => node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } cast
            ? cast.Operand
            : node;
}
