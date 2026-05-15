using System.Linq.Expressions;
using System.Reflection;

namespace Rask.Core.DataGrids;

// Pulls the property name out of an `r => r.Foo` style lambda for use as a sort key.
// Peels the implicit boxing Convert that the compiler inserts when TRow's property is a
// value type returned through `object?`. Rejects method calls, computed expressions, etc.
// — those callers must pass an explicit Key.
internal static class DataGridKeyExtractor
{
    public static string Extract(LambdaExpression expression)
    {
        if (expression is null) throw new ArgumentNullException(nameof(expression));

        var body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } u)
        {
            body = u.Operand;
        }

        if (body is MemberExpression { Member: PropertyInfo prop })
        {
            return prop.Name;
        }

        throw new ArgumentException(
            "Sort expression must be a simple property access (e.g. 'r => r.Name') to auto-derive a key. " +
            $"Pass an explicit Key when sorting on a computed value. Got: {expression}",
            nameof(expression));
    }
}
