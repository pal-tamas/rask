using System.Linq.Expressions;
using System.Reflection;

namespace Rask.Core.Forms;

// Parses Bind / For expressions into a runtime (owner-instance, PropertyInfo) accessor.
// The body must end in a *property* access — anything that produces a value via a property
// getter/setter. The expression *under* that terminal access can be arbitrary: a parameter
// reference, a foreach-captured local, a chain of member accesses, an array indexer, a
// list/dictionary indexer (compiled as MethodCallExpression on get_Item), …
//
// Supported shapes (terminal property in bold):
//   () => p.**Name**                      simple
//   () => p.Address.**Street**            nested member chain (any depth)
//   () => item.**Name**                   foreach-captured local
//   () => p.Items[i].**Name**             list/array indexer
//   () => p.Settings["smtp"].**Host**     dictionary indexer
//   any combination of the above
//
// Rejected shapes (with a precise rejection message pointing at the workaround):
//   () => p.Items[i]                whole list-item bind — no terminal property
//   () => p.GetName()               method call as terminal
//   () => 1 + 1                     not a member access at all
//   () => SomeStatic.Field          static or non-property member
internal static class ExpressionAccessor
{
    public static Accessor Parse(LambdaExpression expression)
    {
        if (expression is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        var body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } u)
        {
            body = u.Operand;
        }

        // Indexer access at the body level — covers `IndexExpression` (custom indexers built
        // via Expression.MakeIndex), `BinaryExpression(ArrayIndex)` (arrays), and the special
        // case of List<T>/Dictionary indexers, which the C# compiler lowers to
        // MethodCallExpression on the synthesised `get_Item` method.
        var isIndexerCall = body is MethodCallExpression mce
                            && mce.Method.IsSpecialName
                            && mce.Method.Name == "get_Item";

        if (isIndexerCall
            || body is IndexExpression
            || body is BinaryExpression { NodeType: ExpressionType.ArrayIndex })
        {
            throw new ArgumentException(
                "Bind expression body is an indexer with no terminal property — Rask binds a property of " +
                $"the indexed item, not the item itself. Use '() => model.Items[i].SomeProperty' instead. Got: {expression}",
                nameof(expression));
        }

        if (body is MethodCallExpression methodCall)
        {
            throw new ArgumentException(
                $"Bind expression body is a method call ('{methodCall.Method.Name}'). Bind requires a property, " +
                $"not a method invocation. Got: {expression}",
                nameof(expression));
        }

        if (body is not MemberExpression me || me.Member is not PropertyInfo prop)
        {
            throw new ArgumentException(
                $"Bind expression must end in a property access. Supported shapes include simple " +
                $"properties, nested chains, foreach-captured locals, and indexer access on the inner " +
                $"expression (e.g. '() => model.Items[i].Name'). Got: {expression}",
                nameof(expression));
        }

        if (me.Expression is null)
        {
            throw new ArgumentException(
                $"Bind expression must access an instance property, not a static one. Got: {expression}",
                nameof(expression));
        }

        var targetGetter = Expression.Lambda<Func<object>>(
            Expression.Convert(me.Expression, typeof(object))).Compile();

        var target = targetGetter()
                     ?? throw new InvalidOperationException(
                         $"Bind expression target evaluated to null: {expression}");

        return new Accessor(
            target,
            prop,
            () => prop.GetValue(target),
            v => prop.SetValue(target, v));
    }

    public sealed record Accessor(
        object Target,
        PropertyInfo Property,
        Func<object?> Getter,
        Action<object?> Setter)
    {
        public Type PropertyType => Property.PropertyType;
        public string PropertyName => Property.Name;
        public FieldIdentifier Field => new(Target, Property.Name);
    }
}
