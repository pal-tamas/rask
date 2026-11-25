using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Rask.Core.Forms;

// What a bound control is named by: the member's whole path from the form's model — `Opening.Quantity`,
// `Parts[0].Start.Quantity` — the path a refusal names it by (FieldPath), never its last member alone, so a member of a
// value object (`f.Opening.Quantity`) never takes the name of a `Quantity` beside it.
internal static class BoundName
{
    // The compiler's field for a `this` a nested lambda captures.
    private const string CapturedThis = "<>4__this";

    /// <summary>
    ///     The name a control bound with <paramref name="bind" /> posts under: its member's path from the model of the
    ///     form it is in, else from the variable the bind starts at.
    /// </summary>
    /// <param name="bind">The bind, as written.</param>
    /// <param name="bound">What <paramref name="bind" /> reads: <see cref="ExpressionAccessor.Parse" />'s.</param>
    internal static string Of(LambdaExpression bind, ExpressionAccessor.Accessor bound)
    {
        if (EditContextScope.Current?.Model is { } model)
        {
            if (ReferenceEquals(model, bound.Target))
            {
                return bound.PropertyName;
            }

            if (FieldPathIndex.Of(model).TryGetValue(bound.Target, out var owner))
            {
                return owner + "." + bound.PropertyName;
            }
        }

        return Written(bind, indexes: true) ?? bound.PropertyName;
    }

    /// <summary>
    ///     The member path <paramref name="bind" /> is written as, from the variable it starts at, which is left out:
    ///     <c>() =&gt; f.Opening.Quantity</c> is <c>Opening.Quantity</c>, <c>() =&gt; _model.Name</c> is <c>Name</c>. Read off
    ///     the expression alone, so it is the same wherever it is asked; null for no member access.
    /// </summary>
    /// <param name="bind">The bind.</param>
    /// <param name="indexes">Whether an index is written (<c>Parts[2].Quantity</c>) or left out (<c>Parts.Quantity</c>).</param>
    internal static string? Written(LambdaExpression? bind, bool indexes)
    {
        if (bind is null || Unwrap(bind.Body) is not MemberExpression terminal)
        {
            return null;
        }

        // The common bind, a member of the variable itself (`() => f.Title`), is its member's name, and allocates nothing more.
        if (terminal.Expression is not { } owner || IsStart(Unwrap(owner)))
        {
            return terminal.Member.Name;
        }

        var segments = new List<string> { terminal.Member.Name };
        for (Expression? node = Unwrap(owner); node is not null && !IsStart(node);)
        {
            node = Step(node, segments, indexes);
        }

        segments.Reverse();
        return string.Concat(segments.Select(static (segment, i) => i == 0 || segment[0] == '[' ? segment : "." + segment));
    }

    // The variable a bind starts at, which its path leaves out: a captured local or parameter (`f`), a member of the
    // component it is written in (`_model`, `Model`), `this` itself, or a lambda's own parameter.
    private static bool IsStart(Expression node) =>
        IsThis(node) || node is ParameterExpression || node is MemberExpression member &&
        (member.Member.DeclaringType is { } declaring && IsClosure(declaring) || member.Expression is null || IsThis(Unwrap(member.Expression)));

    // One segment: adds it, and hands back what it is read from, or null for a shape no path is read through.
    private static Expression? Step(Expression node, List<string> segments, bool indexes) =>
        node switch
        {
            MemberExpression { Expression: { } owner } member => Member(segments, member.Member.Name, owner),
            BinaryExpression { NodeType: ExpressionType.ArrayIndex } element => Indexed(segments, indexes, [element.Right], element.Left),
            MethodCallExpression { Method: { IsSpecialName: true, Name: "get_Item" }, Object: { } indexed } call =>
                Indexed(segments, indexes, call.Arguments, indexed),
            IndexExpression { Object: { } indexed } index => Indexed(segments, indexes, index.Arguments, indexed),
            _ => null,
        };

    private static Expression Member(List<string> segments, string name, Expression owner)
    {
        segments.Add(name);
        return Unwrap(owner);
    }

    private static Expression Indexed(List<string> segments, bool indexes, IReadOnlyList<Expression> arguments, Expression indexed)
    {
        if (indexes)
        {
            segments.Add("[" + string.Join(",", arguments.Select(Shown)) + "]");
        }

        return Unwrap(indexed);
    }

    private static string Shown(Expression argument) =>
        ExpressionAccessor.TryEvaluate(argument, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";

    // `this`: the component the bind is written in, as a constant or as the field a nested lambda keeps it in.
    private static bool IsThis(Expression node) =>
        node is ConstantExpression ||
        node is MemberExpression { Member.Name: CapturedThis } member && member.Member.DeclaringType is { } declaring && IsClosure(declaring);

    private static bool IsClosure(Type type) => type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static Expression Unwrap(Expression node)
    {
        while (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } cast)
        {
            node = cast.Operand;
        }

        return node;
    }
}
