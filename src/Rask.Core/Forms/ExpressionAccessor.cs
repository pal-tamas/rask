using System.Linq.Expressions;
using System.Reflection;

namespace Rask.Core.Forms;

// Public binding API: parses Bind / For expressions into a runtime (owner-instance, PropertyInfo)
// accessor. This is the entry point consumers use to build their own form-bound controls (see the
// MultiSelect sample) — it pairs with BindingHelpers.ResolveBindingContext to wire a control to the
// ambient EditContext.
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
public static class ExpressionAccessor
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
            v => prop.SetValue(target, v))
        {
            Owner = FindRootConstant(me),
        };
    }

    // Walks an expression chain down to its root captured constant and returns its value. For a binding
    // authored in a component as `() => _model.Field`, the field/`this` access compiles to a chain ending
    // in a ConstantExpression holding the component instance — so this returns that component (the
    // *consumer* that owns the binding). When the binding closes over a local instead (the value is a
    // compiler closure, not a component), it returns that — callers treat a non-Component result as "no
    // owner". This is the bound-mode analogue of a callback delegate's Target.
    private static object? FindRootConstant(Expression? e)
    {
        while (e is not null)
        {
            switch (e)
            {
                case ConstantExpression c:
                    return c.Value;
                case MemberExpression m:
                    e = m.Expression;
                    break;
                case UnaryExpression u:
                    e = u.Operand;
                    break;
                case BinaryExpression { NodeType: ExpressionType.ArrayIndex } b:
                    e = b.Left;
                    break;
                case MethodCallExpression { Object: { } obj }:
                    e = obj; // list/dictionary indexer (get_Item)
                    break;
                default:
                    return null;
            }
        }

        return null;
    }

    // Target is the resolved owner instance; Getter/Setter read and write the terminal property on it.
    // Caveat: if the target is a value type (e.g. binding `() => structLocal.Field`), Target is a boxed
    // copy, so Setter writes to that copy, not the original — value-type targets are read-only in practice.
    // Bind to properties of reference-type models (the normal case) for round-tripping writes.
    public sealed record Accessor(
        object Target,
        PropertyInfo Property,
        Func<object?> Getter,
        Action<object?> Setter)
    {
        public Type PropertyType => Property.PropertyType;
        public string PropertyName => Property.Name;
        public FieldIdentifier Field => new(Target, Property.Name);

        // The component that authored the bind expression (the closure root, when it is a component) —
        // used to re-render the consumer on a two-way write so derived UI outside the control/Form
        // updates without any StateHasChanged. Null when the binding closed over a non-component root.
        public object? Owner { get; init; }
    }
}
