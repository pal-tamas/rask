using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

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

        var target = EvaluateTarget(me.Expression)
                     ?? throw new InvalidOperationException(
                         $"Bind expression target evaluated to null: {expression}");

        return new Accessor(target, prop) { Owner = FindRootConstant(me) };
    }

    // Evaluates the target sub-expression (everything left of the terminal property) with plain
    // reflection — no Expression.Compile(). Parse runs on every render of every bound control
    // (Input/Select/Textarea/Bs*), so compiling a throwaway lambda per render was pure overhead;
    // this walks the tree once instead, and needs no runtime code generation under AOT. Covers every
    // documented Bind/For shape: captured closure constants, member chains, foreach-captured locals,
    // and array / list / dictionary indexers. An undocumented shape (e.g. arithmetic inside an index,
    // a method call mid-chain) falls back to compiling the sub-expression — Compile() self-interprets
    // on Mono and is not a RequiresDynamicCode site, so backward compatibility is preserved.
    private static object? EvaluateTarget(Expression e)
    {
        try
        {
            return TryEvaluate(e, out var value) ? value : CompileEvaluate(e);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            // A property getter / indexer reached by reflection (PropertyInfo.GetValue,
            // MethodInfo.Invoke) wraps its exception in TargetInvocationException; the old
            // Expression.Compile() path surfaced the original (e.g. KeyNotFoundException on a missing
            // dictionary key). Unwrap one level so error handling sees the same exception as before.
            ExceptionDispatchInfo.Throw(inner);
            return null; // unreachable — Throw always throws.
        }
    }

    private static bool TryEvaluate(Expression e, out object? value)
    {
        switch (e)
        {
            case ConstantExpression c:
                value = c.Value;
                return true;

            case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u:
                return TryEvaluate(u.Operand, out value);

            case MemberExpression m:
            {
                object? owner = null;
                if (m.Expression is not null && !TryEvaluate(m.Expression, out owner))
                {
                    value = null;
                    return false;
                }

                switch (m.Member)
                {
                    case FieldInfo f:
                        value = f.GetValue(owner);
                        return true;
                    case PropertyInfo p:
                        value = p.GetValue(owner);
                        return true;
                    default:
                        value = null;
                        return false;
                }
            }

            // Array element: p.Items[i] where Items is T[].
            case BinaryExpression { NodeType: ExpressionType.ArrayIndex } b
                when TryEvaluate(b.Left, out var arrObj) && arrObj is Array arr
                     && TryEvaluate(b.Right, out var idxObj) && idxObj is not null:
                value = arr.GetValue(Convert.ToInt32(idxObj, CultureInfo.InvariantCulture));
                return true;

            // Custom indexer via Expression.MakeIndex.
            case IndexExpression { Indexer: { } indexer, Object: { } indexed } ix
                when TryEvaluate(indexed, out var idxObj) && idxObj is not null
                     && TryEvaluateAll(ix.Arguments, out var idxArgs):
                value = indexer.GetValue(idxObj, idxArgs);
                return true;

            // List<T> / Dictionary indexer: the C# compiler lowers these to a get_Item call.
            case MethodCallExpression { Method: { IsSpecialName: true, Name: "get_Item" }, Object: { } receiver } mc
                when TryEvaluate(receiver, out var obj) && obj is not null
                     && TryEvaluateAll(mc.Arguments, out var args):
                value = mc.Method.Invoke(obj, args);
                return true;

            default:
                value = null;
                return false;
        }
    }

    private static bool TryEvaluateAll(System.Collections.Generic.IReadOnlyList<Expression> exprs, out object?[] values)
    {
        values = new object?[exprs.Count];
        for (var i = 0; i < exprs.Count; i++)
        {
            if (!TryEvaluate(exprs[i], out values[i]))
            {
                return false;
            }
        }

        return true;
    }

    // Fallback for the rare undocumented shape. Not a RequiresDynamicCode/IL3050 site — Expression
    // .Compile() falls back to the expression interpreter when the runtime can't emit code.
    private static object? CompileEvaluate(Expression e) =>
        Expression.Lambda<Func<object>>(Expression.Convert(e, typeof(object))).Compile()();

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
    public sealed record Accessor(object Target, PropertyInfo Property)
    {
        /// <summary>Reads the terminal property off <see cref="Target" />.</summary>
        /// <remarks>
        ///     A method rather than the <c>Func&lt;object?&gt;</c> this used to carry. The delegate held
        ///     nothing the record did not already have — it closed over the same <c>Target</c> and
        ///     <c>Property</c> — and building it cost a display class and two delegates on <b>every
        ///     Parse</b>, which is every render of every bound control. Every call site reads
        ///     <c>acc.Getter()</c> either way (#793).
        /// </remarks>
        public object? Getter() => Property.GetValue(Target);

        /// <summary>Writes the terminal property on <see cref="Target" />.</summary>
        /// <remarks>See <see cref="Getter" /> for why this is a method and not an <c>Action</c>.</remarks>
        public void Setter(object? value) => Property.SetValue(Target, value);

        public Type PropertyType => Property.PropertyType;
        public string PropertyName => Property.Name;
        public FieldIdentifier Field => new(Target, Property.Name);

        // The component that authored the bind expression (the closure root, when it is a component) —
        // used to re-render the consumer on a two-way write so derived UI outside the control/Form
        // updates without any StateHasChanged. Null when the binding closed over a non-component root.
        public object? Owner { get; init; }
    }
}
